import 'dart:async';
import 'dart:convert';

import 'package:connectivity_plus/connectivity_plus.dart';
import 'package:shared_preferences/shared_preferences.dart';
import 'package:sqflite/sqflite.dart';
import 'package:supabase_flutter/supabase_flutter.dart';
import 'package:uuid/uuid.dart';

import '../data/app_database.dart';
import 'dealer_balance_apply.dart';
import 'supabase_config.dart';
import 'sync_local_upsert.dart';
import 'sync_meta.dart';
import 'sync_policy.dart';

/// Silent offline-first sync: Mobile ? Supabase ? WinForms.
/// Serialized pass; dependency-safe push; version-ack mark-clean; pull protects dirty/tombstones.
class SyncService {
  SyncService._();
  static final SyncService instance = SyncService._();

  static const _prefsChangeCursor = 'zaib_sync_change_cursor';
  static const _prefsDeviceId = 'zaib_sync_device_id';
  static const _prefsPullBackoffUntil = 'zaib_sync_pull_backoff_until_ms';

  bool _running = false;
  bool _initialized = false;
  bool _queued = false;
  StreamSubscription<List<ConnectivityResult>>? _connSub;
  Timer? _periodic;
  Timer? _dirtyDebounce;
  int _noProgressBackoffMs = 0;

  static const _httpTimeout = Duration(seconds: 12);
  static const _pageSize = 500;
  static const _maxNoProgressBackoffMs = 60000;

  /// When true, remote apply bypasses SyncDirty + timestamp veto (conflict adoption only).
  bool _forceAuthoritativeApply = false;

  SupabaseClient get _client => Supabase.instance.client;

  PostgrestQueryBuilder _table(String name) => _client.from(name);

  Future<void> init() async {
    if (_initialized) return;
    _initialized = true;
    await Supabase.initialize(
      url: SupabaseConfig.url,
      anonKey: SupabaseConfig.anonKey,
    );
    await _ensureDeviceId();
    await _ensureSyncAuxTables();
    SyncMeta.onLocalChange = () {
      _dirtyDebounce?.cancel();
      _dirtyDebounce = Timer(const Duration(seconds: 2), () {
        unawaited(syncNow());
      });
    };
    _connSub = Connectivity().onConnectivityChanged.listen((results) {
      final online = results.isEmpty || results.any((r) => r != ConnectivityResult.none);
      if (online) unawaited(syncNow());
    });
    _periodic = Timer.periodic(const Duration(seconds: 30), (_) {
      unawaited(syncNow());
    });
    unawaited(syncNow());
  }

  void dispose() {
    _connSub?.cancel();
    _periodic?.cancel();
    _dirtyDebounce?.cancel();
  }

  Future<void> _ensureSyncAuxTables() async {
    final db = await AppDatabase.instance.database;
    await db.execute('''
      CREATE TABLE IF NOT EXISTS SyncFailLog (
        Id INTEGER PRIMARY KEY AUTOINCREMENT,
        At TEXT NOT NULL,
        Scope TEXT NOT NULL,
        SyncId TEXT,
        Message TEXT NOT NULL
      )
    ''');
    await db.execute('''
      CREATE TABLE IF NOT EXISTS SyncStagedRemote (
        SyncId TEXT NOT NULL,
        CloudTable TEXT NOT NULL,
        PayloadJson TEXT NOT NULL,
        UpdatedAt TEXT NOT NULL,
        PRIMARY KEY (CloudTable, SyncId)
      )
    ''');
    await db.execute('''
      CREATE TABLE IF NOT EXISTS SyncRejectedUpload (
        Id INTEGER PRIMARY KEY AUTOINCREMENT,
        At TEXT NOT NULL,
        CloudTable TEXT NOT NULL,
        SyncId TEXT NOT NULL,
        LocalUpdatedAt TEXT,
        PayloadJson TEXT NOT NULL,
        ServerPayloadJson TEXT,
        Outcome TEXT NOT NULL
      )
    ''');
    await DealerBalanceApply.ensureTables(db);
    await DealerBalanceApply.backfillMarkers(db);
  }

  Future<String> _ensureDeviceId() async {
    final prefs = await SharedPreferences.getInstance();
    var id = prefs.getString(_prefsDeviceId);
    if (id == null || id.isEmpty) {
      id = const Uuid().v4();
      await prefs.setString(_prefsDeviceId, id);
    }
    return id;
  }

  /// One serialized sync pass. Timeouts on HTTP only � never clear the lock early.
  Future<void> syncNow() async {
    if (_running) {
      _queued = true;
      return;
    }
    _running = true;
    try {
      await _runSyncPass();
    } catch (e) {
      await _logFail('syncNow', null, e);
    } finally {
      _running = false;
      if (_queued) {
        _queued = false;
        unawaited(syncNow());
      }
    }
  }

  Future<void> _runSyncPass() async {
    final net = await Connectivity().checkConnectivity();
    final clearlyOffline =
        net.isNotEmpty && net.every((r) => r == ConnectivityResult.none);
    if (clearlyOffline) return;

    try {
      await AppDatabase.instance.ensureSyncColumnsAndBackfill();
    } catch (_) {}
    await _ensureSyncAuxTables();

    final deviceId = await _ensureDeviceId();

    // Tombstones + dirty first (parents?children), then protected pull.
    // Not a naive order swap: pull still cannot overwrite pending/tombstones.
    try {
      await _pushTombstones(deviceId);
      await _pushAll(deviceId);
    } catch (e) {
      await _logFail('push', null, e);
    }

    try {
      var anyApplied = await _pullChangeFeed();
      anyApplied = await _flushStaged() || anyApplied;
      if (anyApplied) SyncMeta.notifyDataApplied();
    } catch (e) {
      await _logFail('pull', null, e);
    }
  }

  Future<void> _logFail(String scope, String? syncId, Object error) async {
    try {
      final db = await AppDatabase.instance.database;
      final msg = error.toString();
      final safe = msg
          .replaceAll(RegExp(r'eyJ[a-zA-Z0-9_-]+\.[a-zA-Z0-9_-]+\.[a-zA-Z0-9_-]+'), '[redacted-jwt]')
          .replaceAll(RegExp(r'Bearer\s+\S+', caseSensitive: false), 'Bearer [redacted]');
      await db.insert('SyncFailLog', {
        'At': SyncMeta.nowIso(),
        'Scope': scope,
        'SyncId': syncId,
        'Message': safe.length > 500 ? safe.substring(0, 500) : safe,
      });
    } catch (_) {}
  }

  Future<void> _pushAll(String deviceId) async {
    final db = await AppDatabase.instance.database;
    await _pushCustomers(db, deviceId);
    await _pushDealers(db, deviceId);
    await _pushPetrol(db, deviceId);
    await _pushPayouts(db, deviceId);
    await _pushPurchases(db, deviceId);
    await _pushDirect(db, deviceId);
    await _pushStock(db, deviceId);
    await _pushBank(db, deviceId);
    await _pushExpenses(db, deviceId);
    await _pushBalanceOps(db, deviceId);
    await _pushDealerTransfers(db, deviceId);
  }

  /// Acknowledge only the exact uploaded local version (preserve mid-upload edits).
  Future<void> _markCleanIfVersion(
    Database db,
    String table,
    String pkCol,
    Object pk,
    String syncId,
    String uploadedUpdatedAt,
  ) async {
    final rows = await db.query(
      table,
      columns: ['UpdatedAt'],
      where: '$pkCol = ?',
      whereArgs: [pk],
      limit: 1,
    );
    if (rows.isEmpty) return;
    final current = rows.first['UpdatedAt']?.toString();
    if (!SyncPolicy.shouldMarkClean(
      currentUpdatedAt: current,
      uploadedUpdatedAt: uploadedUpdatedAt,
      stillDirtyExpected: false,
    )) {
      return;
    }
    await db.update(
      table,
      {'SyncDirty': 0, 'SyncId': syncId},
      where: '$pkCol = ? AND UpdatedAt = ?',
      whereArgs: [pk, uploadedUpdatedAt],
    );
  }

  Future<String?> _syncIdOf(Database db, String table, String pkCol, Object? pk) async {
    if (pk == null) return null;
    final rows = await db.query(table, columns: ['SyncId'], where: '$pkCol = ?', whereArgs: [pk], limit: 1);
    if (rows.isEmpty) return null;
    final s = rows.first['SyncId']?.toString();
    return (s == null || s.isEmpty) ? null : s;
  }

  Future<bool> _parentReady(Database db, String table, String pkCol, Object? pk) async {
    if (pk == null) return true;
    final rows = await db.query(
      table,
      columns: ['SyncId', 'SyncDirty'],
      where: '$pkCol = ?',
      whereArgs: [pk],
      limit: 1,
    );
    if (rows.isEmpty) return false;
    final syncId = rows.first['SyncId']?.toString();
    if (syncId == null || syncId.isEmpty) return false;
    final dirty = rows.first['SyncDirty'];
    final dirtyInt = dirty is int ? dirty : int.tryParse('$dirty') ?? 0;
    // Parent must already be clean (uploaded) so child FK sync_id is stable on server.
    return dirtyInt == 0;
  }

  Future<UploadAck> _upsertAck(
    String table,
    Map<String, dynamic> row, {
    int? expectedServerRev,
    String? requestId,
  }) async {
    final uploadedAt = row['updated_at']?.toString();
    final uploadedDevice = row['device_id']?.toString();
    final syncId = row['sync_id']?.toString();
    final payload = Map<String, dynamic>.from(row)..remove('server_rev');
    final reqId = requestId ??
        (syncId != null && uploadedAt != null
            ? const Uuid().v5(Namespace.url.value, '$table|$syncId|$uploadedAt')
            : const Uuid().v4());

    Future<SyncApplyOutcome?> callRpc(int? expected) async {
      try {
        final raw = await _client
            .rpc('zaib_sync_apply', params: {
              'p_table': table,
              'p_payload': payload,
              'p_expected_rev': expected,
              'p_request_id': reqId,
              'p_allow_restore': false,
            })
            .timeout(_httpTimeout);
        return SyncApplyOutcome.tryParse(raw);
      } catch (e) {
        await _logFail('rpc:$table', syncId, e);
        return null;
      }
    }

    try {
      final outcome = await callRpc(expectedServerRev);
      if (outcome == null) return UploadAck.unknown;

      // Do NOT blind-retry on expected_rev_required — that would overwrite cloud
      // without proving the local edit was based on that revision. Conflict path
      // stages authoritative server data and keeps local dirty/base version.

      // Stash last outcome metadata on the row map for callers (server_rev).
      if (outcome.serverRev != null) {
        row['_ack_server_rev'] = outcome.serverRev;
      }
      if (outcome.row != null) {
        row['_ack_row'] = outcome.row;
      }
      row['_ack_status'] = outcome.status;
      row['_ack_reason'] = outcome.reason;

      final ack = outcome.ack;
      if (ack == UploadAck.accepted || ack == UploadAck.duplicate) {
        // Still require exact uploaded revision match when representation present.
        if (outcome.updatedAt != null &&
            uploadedAt != null &&
            !SyncPolicy.uploadAckMatches(
              uploadedUpdatedAt: uploadedAt,
              returnedUpdatedAt: outcome.updatedAt,
              uploadedDeviceId: uploadedDevice,
              returnedDeviceId: outcome.deviceId,
            ) &&
            ack == UploadAck.accepted &&
            !outcome.idempotent) {
          // Soft-delete / server may normalize timestamps; trust RPC status for deletes.
          if (payload['deleted_at'] == null) {
            return UploadAck.conflict;
          }
        }
      }
      return ack;
    } catch (e) {
      await _logFail('upsert:$table', syncId, e);
      return UploadAck.unknown;
    }
  }

  /// Preserve rejected local payload; fetch server; reconcile per SyncPolicy conflict rule.
  Future<void> _finishUpload({
    required Database db,
    required String cloudTable,
    required String localTable,
    required String pkCol,
    required Object pk,
    required String syncId,
    required String uploadedUpdatedAt,
    required Map<String, dynamic> uploadedRow,
    required UploadAck ack,
  }) async {
    final ackRev = uploadedRow['_ack_server_rev'];
    // Only stamp ServerRev on definite accept/duplicate — never on conflict observation
    // of a remote row (that would silently advance the local OCC base under a pending edit).
    if ((ack == UploadAck.accepted || ack == UploadAck.duplicate) && ackRev != null) {
      await _storeServerRev(db, localTable, pkCol, pk, _asInt(ackRev));
    }

    if (ack == UploadAck.accepted || ack == UploadAck.duplicate) {
      await _markCleanIfVersion(db, localTable, pkCol, pk, syncId, uploadedUpdatedAt);
      return;
    }
    if (ack == UploadAck.failure || ack == UploadAck.unknown) {
      await _logFail(
        'upload:${ack.name}:$cloudTable',
        syncId,
        ack == UploadAck.unknown
            ? 'unknown commit outcome — keep dirty; retry with same request_id'
            : 'empty/invalid ack or transport failure',
      );
      return;
    }

    if (ack == UploadAck.deleted) {
      // Server says SyncId is deleted — keep local tombstone durable until exact ack.
      // Do not resurrect; stage server evidence; clear dirty only if local still at uploaded version
      // and a matching tombstone/delete intent exists.
      final server = uploadedRow['_ack_row'] is Map
          ? Map<String, dynamic>.from(uploadedRow['_ack_row'] as Map)
          : null;
      await db.insert('SyncRejectedUpload', {
        'At': SyncMeta.nowIso(),
        'CloudTable': cloudTable,
        'SyncId': syncId,
        'LocalUpdatedAt': uploadedUpdatedAt,
        'PayloadJson': jsonEncode(uploadedRow),
        'ServerPayloadJson': server == null ? null : jsonEncode(server),
        'Outcome': 'serverDeleted',
      });
      if (server != null) await _stageRemote(db, cloudTable, server);
      // If this was a tombstone push, leave tombstone; otherwise keep dirty for review.
      return;
    }

    // conflict
    Map<String, dynamic>? server;
    final staged = uploadedRow['_ack_row'];
    if (staged is Map) {
      server = Map<String, dynamic>.from(staged);
    } else {
      try {
        final raw = await _table(cloudTable)
            .select()
            .eq('sync_id', syncId)
            .limit(1)
            .timeout(_httpTimeout);
        if (raw.isNotEmpty) server = Map<String, dynamic>.from(raw.first);
      } catch (e) {
        await _logFail('upload:conflict-fetch:$cloudTable', syncId, e);
      }
    }

    final rows = await db.query(
      localTable,
      columns: ['UpdatedAt'],
      where: '$pkCol = ?',
      whereArgs: [pk],
      limit: 1,
    );
    final localNow = rows.isEmpty ? null : rows.first['UpdatedAt']?.toString();
    final hasTomb = await _hasTombstone(db, syncId);
    final serverDeleted = server != null && _isDeleted(server['deleted_at']);
    final action = SyncPolicy.reconcileRejectedUpload(
      localUpdatedAtNow: localNow,
      rejectedUploadedUpdatedAt: uploadedUpdatedAt,
      hasLocalTombstone: hasTomb,
      serverDeleted: serverDeleted,
    );

    await db.insert('SyncRejectedUpload', {
      'At': SyncMeta.nowIso(),
      'CloudTable': cloudTable,
      'SyncId': syncId,
      'LocalUpdatedAt': uploadedUpdatedAt,
      'PayloadJson': jsonEncode(uploadedRow),
      'ServerPayloadJson': server == null ? null : jsonEncode(server),
      'Outcome': action.name,
    });

    if (server != null) {
      await _stageRemote(db, cloudTable, server);
    }

    if (action == ConflictReconcile.adoptServerClearDirty && server != null) {
      // Atomic adopt: values/deletion + balances + ServerRev + dirty + staged removal.
      // Do not clear dirty merely because a server_rev was observed.
      try {
        await db.transaction((txn) async {
          final still = await txn.query(
            localTable,
            columns: ['UpdatedAt'],
            where: '$pkCol = ?',
            whereArgs: [pk],
            limit: 1,
          );
          // Row may already be gone (delete adopt) — allow when serverDeleted.
          final stillAt = still.isEmpty ? null : still.first['UpdatedAt']?.toString();
          if (still.isNotEmpty &&
              !SyncPolicy.parseTs(stillAt).isAtSameMomentAs(SyncPolicy.parseTs(uploadedUpdatedAt))) {
            // Mid-upload newer local edit — preserve dirty; staged remote retained.
            return;
          }
          if (await _hasTombstone(txn, syncId) && !serverDeleted) {
            return;
          }

          final prev = _forceAuthoritativeApply;
          _forceAuthoritativeApply = true;
          try {
            final result = await _applyChangePayload(txn, cloudTable, server!);
            if (result == RemoteApplyResult.stagedForRetry) {
              throw StateError('adoption blocked (missing parent or protected) — retain dirty');
            }
          } finally {
            _forceAuthoritativeApply = prev;
          }

          await txn.delete(
            'SyncStagedRemote',
            where: 'CloudTable = ? AND SyncId = ?',
            whereArgs: [cloudTable, syncId],
          );
        });
      } catch (e) {
        await _logFail('upload:adopt-rollback:$cloudTable', syncId, e);
        // Transaction rolled back: dirty + staged remain for retry.
      }
    }
  }

  Future<void> _storeServerRev(DatabaseExecutor db, String table, String pkCol, Object pk, int rev) async {
    if (rev <= 0) return;
    try {
      await db.update(table, {'ServerRev': rev}, where: '$pkCol = ?', whereArgs: [pk]);
    } catch (_) {}
  }

  Future<void> _upsertLocal(
    DatabaseExecutor db,
    String table,
    String syncId,
    Map<String, Object?> map, {
    Map<String, dynamic>? remoteRow,
  }) async {
    if (remoteRow != null && remoteRow['server_rev'] != null) {
      map['ServerRev'] = _asInt(remoteRow['server_rev']);
    }
    try {
      await SyncLocalUpsert.upsertBySyncId(db, table, syncId, map);
    } catch (e) {
      await _logFail('upsertLocal:$table', syncId, e);
      rethrow;
    }
  }

  Future<bool> _hasTombstone(DatabaseExecutor db, String syncId) async {
    final rows = await db.query('SyncTombstone', columns: ['SyncId'], where: 'SyncId = ?', whereArgs: [syncId], limit: 1);
    return rows.isNotEmpty;
  }

  /// Remote soft-delete → local delete without leaving SyncTombstone (no push loop).
  Future<void> _applyRemoteDelete(DatabaseExecutor db, String table, String syncId) async {
    final force = _forceAuthoritativeApply;
    if (await _hasTombstone(db, syncId) && !force) {
      // Local delete pending upload — keep tombstone; do not clear until push ack.
      return;
    }
    if (!force) {
      final rows = await db.query(table, columns: ['SyncDirty'], where: 'SyncId = ?', whereArgs: [syncId], limit: 1);
      if (rows.isNotEmpty) {
        final dirty = rows.first['SyncDirty'];
        final dirtyInt = dirty is int ? dirty : int.tryParse('$dirty') ?? 0;
        if (dirtyInt == 1) return;
      }
    }
    if (table == 'DealertoDealer') {
      await DealerBalanceApply.reverse(db: db, sourceSyncId: '$syncId:from', markDealerDirty: false);
      await DealerBalanceApply.reverse(db: db, sourceSyncId: '$syncId:to', markDealerDirty: false);
    }
    if (table == 'DieselLedgerCredit' || table == 'DieselLedgerDebit' || table == 'AddStock') {
      await DealerBalanceApply.reverse(db: db, sourceSyncId: syncId, markDealerDirty: false);
    }
    await db.delete(table, where: 'SyncId = ?', whereArgs: [syncId]);
    await db.delete('SyncTombstone', where: 'SyncId = ?', whereArgs: [syncId]);
  }

  Future<RemoteApplyDecision> _remoteDecision(DatabaseExecutor db,
    String table,
    String syncId,
    String remoteUpdated, {
    required bool remoteDeleted,
    String? remoteDeviceId,
    int? remoteServerRev,
  }) async {
    if (await _hasTombstone(db, syncId)) {
      return SyncPolicy.classifyRemoteApply(
        rowExists: true,
        syncDirty: false,
        hasLocalTombstone: true,
        localUpdatedAt: null,
        remoteUpdatedAt: remoteUpdated,
        localDeviceId: null,
        remoteDeviceId: remoteDeviceId,
        remoteDeleted: remoteDeleted,
        remoteServerRev: remoteServerRev,
        forceAuthoritative: _forceAuthoritativeApply,
      );
    }
    final rows = await db.query(
      table,
      columns: ['UpdatedAt', 'SyncDirty', 'ServerRev'],
      where: 'SyncId = ?',
      whereArgs: [syncId],
      limit: 1,
    );
    if (rows.isEmpty) {
      return SyncPolicy.classifyRemoteApply(
        rowExists: false,
        syncDirty: false,
        hasLocalTombstone: false,
        localUpdatedAt: null,
        remoteUpdatedAt: remoteUpdated,
        localDeviceId: null,
        remoteDeviceId: remoteDeviceId,
        remoteDeleted: remoteDeleted,
        remoteServerRev: remoteServerRev,
        forceAuthoritative: _forceAuthoritativeApply,
      );
    }
    final dirty = rows.first['SyncDirty'];
    final dirtyInt = dirty is int ? dirty : int.tryParse('$dirty') ?? 0;
    return SyncPolicy.classifyRemoteApply(
      rowExists: true,
      syncDirty: dirtyInt == 1,
      hasLocalTombstone: false,
      localUpdatedAt: rows.first['UpdatedAt']?.toString(),
      remoteUpdatedAt: remoteUpdated,
      localDeviceId: await _ensureDeviceId(),
      remoteDeviceId: remoteDeviceId,
      remoteDeleted: remoteDeleted,
      localServerRev: SyncMeta.asServerRev(rows.first['ServerRev']),
      remoteServerRev: remoteServerRev,
      forceAuthoritative: _forceAuthoritativeApply,
    );
  }

  /// null → proceed with apply; otherwise return durable outcome (staging lifecycle).
  Future<RemoteApplyResult?> _gateRemoteApply(
    DatabaseExecutor db,
    String localTable,
    String cloudTable,
    String syncId,
    String updated,
    Map<String, dynamic> r, {
    required bool remoteDeleted,
    String? remoteDeviceId,
  }) async {
    final d = await _remoteDecision(
      db,
      localTable,
      syncId,
      updated,
      remoteDeleted: remoteDeleted,
      remoteDeviceId: remoteDeviceId,
      remoteServerRev: SyncMeta.asServerRev(r['server_rev']),
    );
    if (d == RemoteApplyDecision.apply) return null;
    if (d == RemoteApplyDecision.skipStage) {
      await _stageRemote(db, cloudTable, r);
      return RemoteApplyResult.stagedForRetry;
    }
    return RemoteApplyResult.safelyAlreadyHandled;
  }

  Future<void> _stageRemote(DatabaseExecutor db, String cloudTable, Map<String, dynamic> row) async {
    final syncId = row['sync_id']?.toString() ?? '';
    if (syncId.isEmpty) return;
    await db.insert(
      'SyncStagedRemote',
      {
        'SyncId': syncId,
        'CloudTable': cloudTable,
        'PayloadJson': jsonEncode(row),
        'UpdatedAt': row['updated_at']?.toString() ?? SyncMeta.nowIso(),
      },
      conflictAlgorithm: ConflictAlgorithm.replace,
    );
  }

  Future<void> _pushCustomers(Database db, String deviceId) async {
    final rows = await db.query('AddCustomer', where: 'IFNULL(SyncDirty,1) = 1');
    for (final r in rows) {
      final syncId = (r['SyncId']?.toString().isNotEmpty == true) ? r['SyncId'].toString() : SyncMeta.newId();
      final updatedAt = (r['UpdatedAt']?.toString().isNotEmpty == true) ? r['UpdatedAt'].toString() : SyncMeta.nowIso();
      try {
        final payload = {
          'sync_id': syncId,
          'local_id': r['id'],
          'name': r['Name'] ?? '',
          'mobile': r['Mobile'] ?? '',
          'date_text': r['Date'] ?? '',
          'updated_at': updatedAt,
          'deleted_at': null,
          'device_id': deviceId,
        };
        final expectedRev = r['ServerRev'] == null ? null : _asInt(r['ServerRev']);
        final ack = await _upsertAck('zaib_customers', payload, expectedServerRev: (expectedRev == null || expectedRev == 0) ? null : expectedRev);
        await _finishUpload(
          db: db,
          cloudTable: 'zaib_customers',
          localTable: 'AddCustomer',
          pkCol: 'id',
          pk: r['id']!,
          syncId: syncId,
          uploadedUpdatedAt: updatedAt,
          uploadedRow: payload,
          ack: ack,
        );
      } catch (e) {
        await _logFail('push:zaib_customers', syncId, e);
      }
    }
  }

  Future<void> _pushDealers(Database db, String deviceId) async {
    final rows = await db.query('AddDealer', where: 'IFNULL(SyncDirty,1) = 1');
    for (final r in rows) {
      final syncId = (r['SyncId']?.toString().isNotEmpty == true) ? r['SyncId'].toString() : SyncMeta.newId();
      final updatedAt = (r['UpdatedAt']?.toString().isNotEmpty == true) ? r['UpdatedAt'].toString() : SyncMeta.nowIso();
      try {
        final payload = {
          'sync_id': syncId,
          'local_id': r['Did'],
          'dealer_name': r['DealerName'] ?? '',
          'dd_amount': r['DDAmount'] ?? 0,
          'd_amount': r['DAmount'] ?? 0,
          'date_text': r['Date'] ?? '',
          'updated_at': updatedAt,
          'deleted_at': null,
          'device_id': deviceId,
        };
        final expectedRev = r['ServerRev'] == null ? null : _asInt(r['ServerRev']);
        final ack = await _upsertAck('zaib_dealers', payload, expectedServerRev: (expectedRev == null || expectedRev == 0) ? null : expectedRev);
        await _finishUpload(
          db: db,
          cloudTable: 'zaib_dealers',
          localTable: 'AddDealer',
          pkCol: 'Did',
          pk: r['Did']!,
          syncId: syncId,
          uploadedUpdatedAt: updatedAt,
          uploadedRow: payload,
          ack: ack,
        );
      } catch (e) {
        await _logFail('push:zaib_dealers', syncId, e);
      }
    }
  }

  Future<void> _pushPetrol(Database db, String deviceId) async {
    final rows = await db.query('PetrolAdd', where: 'IFNULL(SyncDirty,1) = 1');
    for (final r in rows) {
      if (!await _parentReady(db, 'AddCustomer', 'id', r['CustomerId'])) continue;
      final syncId = (r['SyncId']?.toString().isNotEmpty == true) ? r['SyncId'].toString() : SyncMeta.newId();
      final updatedAt = (r['UpdatedAt']?.toString().isNotEmpty == true) ? r['UpdatedAt'].toString() : SyncMeta.nowIso();
      final custSync = await _syncIdOf(db, 'AddCustomer', 'id', r['CustomerId']);
      String customerName = '';
      if (r['CustomerId'] != null) {
        final c = await db.query('AddCustomer', columns: ['Name'], where: 'id = ?', whereArgs: [r['CustomerId']], limit: 1);
        if (c.isNotEmpty) customerName = c.first['Name']?.toString() ?? '';
      }
      try {
        final payload = {
          'sync_id': syncId,
          'local_id': r['pid'],
          'customer_sync_id': custSync,
          'customer_name': customerName,
          'date_text': r['Date'] ?? '',
          'receipt_no': r['ReceiptNo'] ?? '',
          'vehicle': r['vehicle'] ?? '',
          'litter': r['Litter'] ?? 0,
          'rate': r['Rate'] ?? 0,
          'advance': r['Advance'] ?? 0,
          'amount': r['Amount'] ?? 0,
          'credit': r['Credit'] ?? 0,
          'balance': r['Balance'] ?? 0,
          'note': r['Note'] ?? '',
          'is_initial_entry': r['IsInitialEntry'] ?? 1,
          'processed': r['Processed'] ?? 0,
          'updated_at': updatedAt,
          'deleted_at': null,
          'device_id': deviceId,
        };
        final expectedRev = r['ServerRev'] == null ? null : _asInt(r['ServerRev']);
        final ack = await _upsertAck('zaib_petrol_entries', payload, expectedServerRev: (expectedRev == null || expectedRev == 0) ? null : expectedRev);
        await _finishUpload(
          db: db,
          cloudTable: 'zaib_petrol_entries',
          localTable: 'PetrolAdd',
          pkCol: 'pid',
          pk: r['pid']!,
          syncId: syncId,
          uploadedUpdatedAt: updatedAt,
          uploadedRow: payload,
          ack: ack,
        );
      } catch (e) {
        await _logFail('push:zaib_petrol_entries', syncId, e);
      }
    }
  }

  Future<void> _pushPayouts(Database db, String deviceId) async {
    final rows = await db.query('DieselLedgerCredit', where: 'IFNULL(SyncDirty,1) = 1');
    for (final r in rows) {
      if (!await _parentReady(db, 'AddDealer', 'Did', r['Did'])) continue;
      final syncId = (r['SyncId']?.toString().isNotEmpty == true) ? r['SyncId'].toString() : SyncMeta.newId();
      final updatedAt = (r['UpdatedAt']?.toString().isNotEmpty == true) ? r['UpdatedAt'].toString() : SyncMeta.nowIso();
      final dSync = await _syncIdOf(db, 'AddDealer', 'Did', r['Did']);
      String dealerName = '';
      if (r['Did'] != null) {
        final d = await db.query('AddDealer', columns: ['DealerName'], where: 'Did = ?', whereArgs: [r['Did']], limit: 1);
        if (d.isNotEmpty) dealerName = d.first['DealerName']?.toString() ?? '';
      }
      try {
        final payload = {
          'sync_id': syncId,
          'local_id': r['LedgerID'],
          'dealer_sync_id': dSync,
          'dealer_name': dealerName,
          'amount_given': r['AmounGiven'] ?? 0,
          'date_text': r['Date'] ?? '',
          'note': r['Note'] ?? '',
          'updated_at': updatedAt,
          'deleted_at': null,
          'device_id': deviceId,
        };
        final expectedRev = r['ServerRev'] == null ? null : _asInt(r['ServerRev']);
        final ack = await _upsertAck('zaib_dealer_payouts', payload, expectedServerRev: (expectedRev == null || expectedRev == 0) ? null : expectedRev);
        await _finishUpload(
          db: db,
          cloudTable: 'zaib_dealer_payouts',
          localTable: 'DieselLedgerCredit',
          pkCol: 'LedgerID',
          pk: r['LedgerID']!,
          syncId: syncId,
          uploadedUpdatedAt: updatedAt,
          uploadedRow: payload,
          ack: ack,
        );
      } catch (e) {
        await _logFail('push:zaib_dealer_payouts', syncId, e);
      }
    }
  }

  Future<void> _pushPurchases(Database db, String deviceId) async {
    final rows = await db.query('AddStock', where: 'IFNULL(SyncDirty,1) = 1');
    for (final r in rows) {
      if (!await _parentReady(db, 'AddDealer', 'Did', r['DealerId'])) continue;
      final syncId = (r['SyncId']?.toString().isNotEmpty == true) ? r['SyncId'].toString() : SyncMeta.newId();
      final updatedAt = (r['UpdatedAt']?.toString().isNotEmpty == true) ? r['UpdatedAt'].toString() : SyncMeta.nowIso();
      final dSync = await _syncIdOf(db, 'AddDealer', 'Did', r['DealerId']);
      String dealerName = '';
      if (r['DealerId'] != null) {
        final d = await db.query('AddDealer', columns: ['DealerName'], where: 'Did = ?', whereArgs: [r['DealerId']], limit: 1);
        if (d.isNotEmpty) dealerName = d.first['DealerName']?.toString() ?? '';
      }
      try {
        final payload = {
          'sync_id': syncId,
          'local_id': r['Sid'],
          'dealer_sync_id': dSync,
          'dealer_name': dealerName,
          'vehicle': r['Vehicle'] ?? '',
          'rate': r['Rate'] ?? 0,
          'add_diesel': r['AddDisel'] ?? 0,
          'date_text': r['Date'] ?? '',
          'note': r['Note'] ?? '',
          'updated_at': updatedAt,
          'deleted_at': null,
          'device_id': deviceId,
        };
        final expectedRev = r['ServerRev'] == null ? null : _asInt(r['ServerRev']);
        final ack = await _upsertAck('zaib_dealer_purchases', payload, expectedServerRev: (expectedRev == null || expectedRev == 0) ? null : expectedRev);
        await _finishUpload(
          db: db,
          cloudTable: 'zaib_dealer_purchases',
          localTable: 'AddStock',
          pkCol: 'Sid',
          pk: r['Sid']!,
          syncId: syncId,
          uploadedUpdatedAt: updatedAt,
          uploadedRow: payload,
          ack: ack,
        );
      } catch (e) {
        await _logFail('push:zaib_dealer_purchases', syncId, e);
      }
    }
  }

  Future<void> _pushDirect(Database db, String deviceId) async {
    final rows = await db.query('DieselLedgerDebit', where: 'IFNULL(SyncDirty,1) = 1');
    for (final r in rows) {
      if (!await _parentReady(db, 'AddDealer', 'Did', r['Did'])) continue;
      final syncId = (r['SyncId']?.toString().isNotEmpty == true) ? r['SyncId'].toString() : SyncMeta.newId();
      final updatedAt = (r['UpdatedAt']?.toString().isNotEmpty == true) ? r['UpdatedAt'].toString() : SyncMeta.nowIso();
      final dSync = await _syncIdOf(db, 'AddDealer', 'Did', r['Did']);
      String dealerName = '';
      if (r['Did'] != null) {
        final d = await db.query('AddDealer', columns: ['DealerName'], where: 'Did = ?', whereArgs: [r['Did']], limit: 1);
        if (d.isNotEmpty) dealerName = d.first['DealerName']?.toString() ?? '';
      }
      try {
        final payload = {
          'sync_id': syncId,
          'local_id': r['LedgerID'],
          'dealer_sync_id': dSync,
          'dealer_name': dealerName,
          'amount_given': r['AmounGiven'] ?? 0,
          'date_text': r['Date'] ?? '',
          'note': r['Note'] ?? '',
          'updated_at': updatedAt,
          'deleted_at': null,
          'device_id': deviceId,
        };
        final expectedRev = r['ServerRev'] == null ? null : _asInt(r['ServerRev']);
        final ack = await _upsertAck('zaib_dealer_direct', payload, expectedServerRev: (expectedRev == null || expectedRev == 0) ? null : expectedRev);
        await _finishUpload(
          db: db,
          cloudTable: 'zaib_dealer_direct',
          localTable: 'DieselLedgerDebit',
          pkCol: 'LedgerID',
          pk: r['LedgerID']!,
          syncId: syncId,
          uploadedUpdatedAt: updatedAt,
          uploadedRow: payload,
          ack: ack,
        );
      } catch (e) {
        await _logFail('push:zaib_dealer_direct', syncId, e);
      }
    }
  }

  Future<void> _pushStock(Database db, String deviceId) async {
    final rows = await db.query('StockDiesel', where: 'IFNULL(SyncDirty,1) = 1');
    for (final r in rows) {
      if (!await _parentReady(db, 'AddDealer', 'Did', r['SDid'])) continue;
      final syncId = (r['SyncId']?.toString().isNotEmpty == true) ? r['SyncId'].toString() : SyncMeta.newId();
      final updatedAt = (r['UpdatedAt']?.toString().isNotEmpty == true) ? r['UpdatedAt'].toString() : SyncMeta.nowIso();
      final dSync = await _syncIdOf(db, 'AddDealer', 'Did', r['SDid']);
      String dealerName = '';
      if (r['SDid'] != null) {
        final d = await db.query('AddDealer', columns: ['DealerName'], where: 'Did = ?', whereArgs: [r['SDid']], limit: 1);
        if (d.isNotEmpty) dealerName = d.first['DealerName']?.toString() ?? '';
      }
      try {
        final payload = {
          'sync_id': syncId,
          'local_id': r['SID'],
          'dealer_sync_id': dSync,
          'dealer_name': dealerName,
          'date_text': r['Date'] ?? '',
          'vehicle': r['Vehicle'] ?? '',
          'litter': r['Litter'] ?? 0,
          'rate': r['Rate'] ?? 0,
          'credit': r['Credit'] ?? 0,
          'debit': r['Debit'] ?? 0,
          'note': r['Note'] ?? '',
          'updated_at': updatedAt,
          'deleted_at': null,
          'device_id': deviceId,
        };
        final expectedRev = r['ServerRev'] == null ? null : _asInt(r['ServerRev']);
        final ack = await _upsertAck('zaib_stock_diesel', payload, expectedServerRev: (expectedRev == null || expectedRev == 0) ? null : expectedRev);
        await _finishUpload(
          db: db,
          cloudTable: 'zaib_stock_diesel',
          localTable: 'StockDiesel',
          pkCol: 'SID',
          pk: r['SID']!,
          syncId: syncId,
          uploadedUpdatedAt: updatedAt,
          uploadedRow: payload,
          ack: ack,
        );
      } catch (e) {
        await _logFail('push:zaib_stock_diesel', syncId, e);
      }
    }
  }

  Future<void> _pushBank(Database db, String deviceId) async {
    final rows = await db.query('BankTransactions', where: 'IFNULL(SyncDirty,1) = 1');
    for (final r in rows) {
      if (r['CustomerId'] != null && !await _parentReady(db, 'AddCustomer', 'id', r['CustomerId'])) continue;
      if (r['DealerId'] != null && !await _parentReady(db, 'AddDealer', 'Did', r['DealerId'])) continue;
      final syncId = (r['SyncId']?.toString().isNotEmpty == true) ? r['SyncId'].toString() : SyncMeta.newId();
      final updatedAt = (r['UpdatedAt']?.toString().isNotEmpty == true) ? r['UpdatedAt'].toString() : SyncMeta.nowIso();
      final cSync = await _syncIdOf(db, 'AddCustomer', 'id', r['CustomerId']);
      final dSync = await _syncIdOf(db, 'AddDealer', 'Did', r['DealerId']);
      String customerName = '';
      String dealerName = '';
      if (r['CustomerId'] != null) {
        final c = await db.query('AddCustomer', columns: ['Name'], where: 'id = ?', whereArgs: [r['CustomerId']], limit: 1);
        if (c.isNotEmpty) customerName = c.first['Name']?.toString() ?? '';
      }
      if (r['DealerId'] != null) {
        final d = await db.query('AddDealer', columns: ['DealerName'], where: 'Did = ?', whereArgs: [r['DealerId']], limit: 1);
        if (d.isNotEmpty) dealerName = d.first['DealerName']?.toString() ?? '';
      }
      try {
        final payload = {
          'sync_id': syncId,
          'local_id': r['Id'],
          'transaction_date': r['TransactionDate'] ?? '',
          'transaction_type': r['TransactionType'] ?? '',
          'customer_sync_id': cSync,
          'customer_name': customerName,
          'dealer_sync_id': dSync,
          'dealer_name': dealerName,
          'amount': r['Amount'] ?? 0,
          'note': r['Note'] ?? '',
          'bank_name': r['BankName'] ?? '',
          'updated_at': updatedAt,
          'deleted_at': null,
          'device_id': deviceId,
        };
        final expectedRev = r['ServerRev'] == null ? null : _asInt(r['ServerRev']);
        final ack = await _upsertAck('zaib_bank_transactions', payload, expectedServerRev: (expectedRev == null || expectedRev == 0) ? null : expectedRev);
        await _finishUpload(
          db: db,
          cloudTable: 'zaib_bank_transactions',
          localTable: 'BankTransactions',
          pkCol: 'Id',
          pk: r['Id']!,
          syncId: syncId,
          uploadedUpdatedAt: updatedAt,
          uploadedRow: payload,
          ack: ack,
        );
      } catch (e) {
        await _logFail('push:zaib_bank_transactions', syncId, e);
      }
    }
  }

  Future<void> _pushExpenses(Database db, String deviceId) async {
    final rows = await db.query('Expensetable', where: 'IFNULL(SyncDirty,1) = 1');
    for (final r in rows) {
      final syncId = (r['SyncId']?.toString().isNotEmpty == true) ? r['SyncId'].toString() : SyncMeta.newId();
      final updatedAt = (r['UpdatedAt']?.toString().isNotEmpty == true) ? r['UpdatedAt'].toString() : SyncMeta.nowIso();
      try {
        final payload = {
          'sync_id': syncId,
          'local_id': r['sid'],
          'name': r['Name'] ?? '',
          'category': r['Category'] ?? '',
          'amount': r['Amount'] ?? 0,
          'e_date': r['EDate'] ?? '',
          'note': r['Note'] ?? '',
          'updated_at': updatedAt,
          'deleted_at': null,
          'device_id': deviceId,
        };
        final expectedRev = r['ServerRev'] == null ? null : _asInt(r['ServerRev']);
        final ack = await _upsertAck('zaib_expenses', payload, expectedServerRev: (expectedRev == null || expectedRev == 0) ? null : expectedRev);
        await _finishUpload(
          db: db,
          cloudTable: 'zaib_expenses',
          localTable: 'Expensetable',
          pkCol: 'sid',
          pk: r['sid']!,
          syncId: syncId,
          uploadedUpdatedAt: updatedAt,
          uploadedRow: payload,
          ack: ack,
        );
      } catch (e) {
        await _logFail('push:zaib_expenses', syncId, e);
      }
    }
  }

  Future<void> _pushBalanceOps(Database db, String deviceId) async {
    final rows = await db.query('SyncDealerBalanceOp', where: 'IFNULL(SyncDirty,1) = 1 AND DeletedAt IS NULL');
    for (final r in rows) {
      final syncId = r['SyncId']?.toString() ?? '';
      if (syncId.isEmpty) continue;
      final updatedAt = (r['UpdatedAt']?.toString().isNotEmpty == true) ? r['UpdatedAt'].toString() : SyncMeta.nowIso();
      try {
        final payload = {
          'sync_id': syncId,
          'dealer_sync_id': r['DealerSyncId'],
          'dd_delta': r['DdDelta'] ?? 0,
          'd_delta': r['DDelta'] ?? 0,
          'source_kind': r['SourceKind'] ?? 'manual',
          'source_sync_id': r['SourceSyncId'],
          'date_text': r['DateText'],
          'note': r['Note'],
          'updated_at': updatedAt,
          'deleted_at': null,
          'device_id': deviceId,
        };
        final expectedRev = r['ServerRev'] == null ? null : _asInt(r['ServerRev']);
        final ack = await _upsertAck(
          'zaib_dealer_balance_ops',
          payload,
          expectedServerRev: (expectedRev == null || expectedRev == 0) ? null : expectedRev,
        );
        if (payload['_ack_server_rev'] != null) {
          await db.update(
            'SyncDealerBalanceOp',
            {'ServerRev': _asInt(payload['_ack_server_rev'])},
            where: 'SyncId = ?',
            whereArgs: [syncId],
          );
        }
        if (ack == UploadAck.accepted || ack == UploadAck.duplicate) {
          final still = await db.query(
            'SyncDealerBalanceOp',
            columns: ['UpdatedAt'],
            where: 'SyncId = ?',
            whereArgs: [syncId],
            limit: 1,
          );
          if (still.isNotEmpty && still.first['UpdatedAt']?.toString() == updatedAt) {
            await db.update('SyncDealerBalanceOp', {'SyncDirty': 0}, where: 'SyncId = ?', whereArgs: [syncId]);
          }
        }
      } catch (e) {
        await _logFail('push:zaib_dealer_balance_ops', syncId, e);
      }
    }
  }

  Future<void> _pushTombstones(String deviceId) async {
    final db = await AppDatabase.instance.database;
    final rows = await db.query('SyncTombstone');
    for (final r in rows) {
      final table = r['CloudTable']?.toString() ?? '';
      final syncId = r['SyncId']?.toString() ?? '';
      if (table.isEmpty || syncId.isEmpty) continue;
      final deletedAt = r['DeletedAt']?.toString() ?? SyncMeta.nowIso();
      final expectedRev = SyncMeta.asServerRev(r['ExpectedServerRev']);
      final requestId = (r['RequestId']?.toString().isNotEmpty == true)
          ? r['RequestId'].toString()
          : SyncMeta.tombstoneRequestId(table, syncId, deletedAt);
      try {
        final payload = {
          'sync_id': syncId,
          'updated_at': deletedAt,
          'deleted_at': deletedAt,
          'device_id': deviceId,
        };
        // OCC: use stored base revision when known. Never invent/learn a newer rev to overwrite.
        final ack = await _upsertAck(
          table,
          payload,
          expectedServerRev: expectedRev,
          requestId: requestId,
        );
        if (ack == UploadAck.accepted ||
            ack == UploadAck.duplicate ||
            ack == UploadAck.deleted) {
          // Clear only the exact delete operation version that was acknowledged.
          await db.delete(
            'SyncTombstone',
            where: 'SyncId = ? AND DeletedAt = ?',
            whereArgs: [syncId, deletedAt],
          );
        } else if (ack == UploadAck.conflict) {
          // Legacy missing base rev OR concurrent edit: check deletion evidence only.
          // Do NOT fetch a newer revision and blind-retry the delete.
          try {
            final raw = await _table(table)
                .select('deleted_at,server_rev')
                .eq('sync_id', syncId)
                .limit(1)
                .timeout(_httpTimeout);
            if (raw.isNotEmpty && raw.first['deleted_at'] != null) {
              await db.delete(
                'SyncTombstone',
                where: 'SyncId = ? AND DeletedAt = ?',
                whereArgs: [syncId, deletedAt],
              );
            } else {
              await _logFail(
                'push:tombstone:conflict:$table',
                syncId,
                expectedRev == null
                    ? 'legacy tombstone missing ExpectedServerRev — retain delete intent; needs explicit resolution'
                    : 'delete conflict (rev_mismatch) — retain tombstone; do not overwrite cloud',
              );
              if (raw.isNotEmpty) {
                await _stageRemote(db, table, Map<String, dynamic>.from(raw.first));
              }
            }
          } catch (e) {
            await _logFail('push:tombstone:conflict-check:$table', syncId, e);
          }
        }
        // unknown/failure: keep tombstone durable for retry with same request_id
      } catch (e) {
        await _logFail('push:tombstone:$table', syncId, e);
      }
    }
  }

  Future<int> _loadChangeCursor() async {
    final prefs = await SharedPreferences.getInstance();
    final raw = prefs.getString(_prefsChangeCursor);
    if (raw == null || raw.isEmpty) {
      // Brand-new device: start at 0 and replay authoritative feed (no MAX skip).
      await _saveChangeCursor(0);
      return 0;
    }
    final decoded = SyncPolicy.decodeChangeCursor(raw);
    if (decoded == -1) {
      // Legacy chg:/timestamp/MAX-bootstrap: repair by replaying feed from 0.
      // LWW + SyncBalanceApplied keep financial effects idempotent.
      await _logFail('pull:bootstrap_repair', null, 'legacy cursor → chg_v2:0');
      await _saveChangeCursor(0);
      return 0;
    }
    return decoded;
  }

  Future<void> _saveChangeCursor(int rev) async {
    final prefs = await SharedPreferences.getInstance();
    await prefs.setString(_prefsChangeCursor, SyncPolicy.encodeChangeCursor(rev));
  }

  Future<bool> _pullBackoffActive() async {
    final prefs = await SharedPreferences.getInstance();
    final until = prefs.getInt(_prefsPullBackoffUntil) ?? 0;
    return DateTime.now().millisecondsSinceEpoch < until;
  }

  Future<void> _setPullBackoff(int ms) async {
    final prefs = await SharedPreferences.getInstance();
    final until = DateTime.now().millisecondsSinceEpoch + ms;
    await prefs.setInt(_prefsPullBackoffUntil, until);
  }

  Future<void> _clearPullBackoff() async {
    final prefs = await SharedPreferences.getInstance();
    await prefs.remove(_prefsPullBackoffUntil);
    _noProgressBackoffMs = 0;
  }

  Future<List<Map<String, dynamic>>> _fetchChangePage(int cursor) async {
    final raw = await _table('zaib_sync_feed')
        .select()
        .gt('rev', cursor)
        .order('rev', ascending: true)
        .limit(_pageSize)
        .timeout(_httpTimeout);
    return raw.map((row) => Map<String, dynamic>.from(row as Map)).toList();
  }

  Map<String, dynamic> _payloadFromChangeRow(Map<String, dynamic> changeRow) {
    final rowSyncId = changeRow['row_sync_id']?.toString() ?? '';
    final op = changeRow['op']?.toString() ?? '';
    final payloadRaw = changeRow['payload'];
    Map<String, dynamic> r;
    if (payloadRaw is Map) {
      r = Map<String, dynamic>.from(payloadRaw);
    } else if (payloadRaw is String && payloadRaw.isNotEmpty) {
      r = Map<String, dynamic>.from(jsonDecode(payloadRaw) as Map);
    } else {
      r = {};
    }
    if (rowSyncId.isNotEmpty && (r['sync_id'] == null || r['sync_id'].toString().isEmpty)) {
      r['sync_id'] = rowSyncId;
    }
    if (op == 'delete' && !_isDeleted(r['deleted_at'])) {
      r['deleted_at'] = changeRow['updated_at'] ??
          changeRow['created_at'] ??
          SyncMeta.nowIso();
    }
    return r;
  }

  Future<RemoteApplyResult> _applyChangePayload(
    DatabaseExecutor db,
    String cloudTable,
    Map<String, dynamic> r,
  ) async {
    switch (cloudTable) {
      case 'zaib_customers':
        return _applyCustomer(db, r);
      case 'zaib_dealers':
        return _applyDealer(db, r);
      case 'zaib_petrol_entries':
        return _applyPetrol(db, r);
      case 'zaib_dealer_payouts':
        return _applyPayout(db, r);
      case 'zaib_dealer_purchases':
        return _applyPurchase(db, r);
      case 'zaib_dealer_direct':
        return _applyDirect(db, r);
      case 'zaib_stock_diesel':
        return _applyStock(db, r);
      case 'zaib_bank_transactions':
        return _applyBank(db, r);
      case 'zaib_expenses':
        return _applyExpense(db, r);
      case 'zaib_dealer_balance_ops':
        return _applyBalanceOp(db, r);
      case 'zaib_dealer_transfers':
        return _applyDealerTransfer(db, r);
      default:
        throw StateError('unknown cloud_table $cloudTable');
    }
  }

  /// Change-feed pull against zaib_sync_feed.rev (protocol v2).
  /// Returns true if any row was [RemoteApplyResult.applied].
  Future<bool> _pullChangeFeed() async {
    if (await _pullBackoffActive()) {
      await _logFail('pull:backoff', null, 'skip pass (no-progress backoff)');
      return false;
    }

    final db = await AppDatabase.instance.database;
    var cursor = await _loadChangeCursor();
    var anyApplied = false;

    while (true) {
      List<Map<String, dynamic>> page;
      try {
        page = await _fetchChangePage(cursor);
      } catch (e) {
        await _logFail('pull:change_feed', null, e);
        break;
      }
      if (page.isEmpty) {
        await _clearPullBackoff();
        break;
      }

      final successfulIds = <int>[];
      final cursorBefore = cursor;

      for (final changeRow in page) {
        final rev = _asInt(changeRow['rev']);
        if (rev <= 0) continue;
        final cloudTable = changeRow['cloud_table']?.toString() ?? '';
        final rowSyncId = changeRow['row_sync_id']?.toString() ?? '';
        final payload = _payloadFromChangeRow(changeRow);

        RemoteApplyResult result;
        try {
          result = await db.transaction(
            (txn) => _applyChangePayload(txn, cloudTable, payload),
          );
        } catch (e) {
          // Do not advance past an unhandled failure; retain staging from prior successes.
          await _logFail('pull:apply:$cloudTable', rowSyncId, e);
          break;
        }

        successfulIds.add(rev);
        if (result == RemoteApplyResult.applied) anyApplied = true;
      }

      final newCursor = SyncPolicy.advanceContiguousWatermark(
        cursor: cursor,
        seenIds: successfulIds,
      );
      if (newCursor > cursor) {
        cursor = newCursor;
        await _saveChangeCursor(cursor);
        await _clearPullBackoff();
      }

      if (SyncPolicy.changeFeedNoProgress(
        cursorBefore: cursorBefore,
        cursorAfter: newCursor,
        rawPageLength: page.length,
        pageSize: _pageSize,
      )) {
        _noProgressBackoffMs = _noProgressBackoffMs == 0
            ? 2000
            : (_noProgressBackoffMs * 2).clamp(2000, _maxNoProgressBackoffMs);
        await _setPullBackoff(_noProgressBackoffMs);
        await _logFail(
          'pull:no_progress',
          null,
          'cursor=$cursorBefore page=${page.length} backoff=${_noProgressBackoffMs}ms',
        );
        break;
      }

      if (!SyncPolicy.serverPageHasMore(rawPageLength: page.length, pageSize: _pageSize)) {
        break;
      }
    }

    return anyApplied;
  }

  Future<bool> _flushStaged() async {
    final db = await AppDatabase.instance.database;
    final staged = await db.query('SyncStagedRemote', orderBy: 'UpdatedAt ASC');
    var anyApplied = false;
    for (final s in staged) {
      final cloud = s['CloudTable']?.toString() ?? '';
      final json = s['PayloadJson']?.toString() ?? '';
      final stagedSync = s['SyncId']?.toString() ?? '';
      if (cloud.isEmpty || json.isEmpty) continue;
      try {
        final row = Map<String, dynamic>.from(jsonDecode(json) as Map);
        await db.transaction((txn) async {
          final result = await _applyChangePayload(txn, cloud, row);
          if (result == RemoteApplyResult.applied) anyApplied = true;
          if (SyncPolicy.shouldRemoveStagedRemote(result) && stagedSync.isNotEmpty) {
            await txn.delete(
              'SyncStagedRemote',
              where: 'CloudTable = ? AND SyncId = ?',
              whereArgs: [cloud, stagedSync],
            );
          }
        });
      } catch (e) {
        await _logFail('flush:$cloud', stagedSync, e);
      }
    }
    return anyApplied;
  }

  int _asInt(dynamic v, [int fallback = 0]) {
    if (v == null) return fallback;
    if (v is int) return v;
    if (v is bool) return v ? 1 : 0;
    if (v is num) return v.toInt();
    return int.tryParse(v.toString()) ?? fallback;
  }

  double _asDouble(dynamic v, [double fallback = 0]) {
    if (v == null) return fallback;
    if (v is double) return v;
    if (v is num) return v.toDouble();
    return double.tryParse(v.toString()) ?? fallback;
  }

  String _asStr(dynamic v) => v?.toString() ?? '';

  bool _isDeleted(dynamic v) {
    if (v == null) return false;
    final s = v.toString().trim();
    return s.isNotEmpty && s.toLowerCase() != 'null';
  }

  Future<int?> _localIdBySync(DatabaseExecutor db, String table, String pkCol, String? syncId) async {
    if (syncId == null || syncId.isEmpty) return null;
    final rows = await db.query(table, columns: [pkCol], where: 'SyncId = ?', whereArgs: [syncId], limit: 1);
    if (rows.isEmpty) return null;
    final v = rows.first[pkCol];
    if (v is int) return v;
    return int.tryParse('$v');
  }

  Future<RemoteApplyResult> _applyCustomer(DatabaseExecutor db, Map<String, dynamic> r) async {
    final syncId = r['sync_id']?.toString() ?? '';
    if (syncId.isEmpty) return RemoteApplyResult.safelyAlreadyHandled;
    final updated = r['updated_at']?.toString() ?? SyncMeta.nowIso();
    final deleted = _isDeleted(r['deleted_at']);
    final device = r['device_id']?.toString();
    final gate = await _gateRemoteApply(
      db,
      'AddCustomer',
      'zaib_customers',
      syncId,
      updated,
      r,
      remoteDeleted: deleted,
      remoteDeviceId: device,
    );
    if (gate != null) return gate;
    if (deleted) {
      await _applyRemoteDelete(db, 'AddCustomer', syncId);
      return RemoteApplyResult.applied;
    }
    await _upsertLocal(db, 'AddCustomer', syncId, {
      'Name': _asStr(r['name']),
      'Mobile': _asStr(r['mobile']),
      'Date': _asStr(r['date_text']),
      'SyncId': syncId,
      'UpdatedAt': updated,
      'SyncDirty': 0,
    }, remoteRow: r);
    return RemoteApplyResult.applied;
  }

  Future<RemoteApplyResult> _applyDealer(DatabaseExecutor db, Map<String, dynamic> r) async {
    final syncId = r['sync_id']?.toString() ?? '';
    if (syncId.isEmpty) return RemoteApplyResult.safelyAlreadyHandled;
    final updated = r['updated_at']?.toString() ?? SyncMeta.nowIso();
    final deleted = _isDeleted(r['deleted_at']);
    final device = r['device_id']?.toString();
    final gate = await _gateRemoteApply(
      db,
      'AddDealer',
      'zaib_dealers',
      syncId,
      updated,
      r,
      remoteDeleted: deleted,
      remoteDeviceId: device,
    );
    if (gate != null) return gate;
    if (deleted) {
      await _applyRemoteDelete(db, 'AddDealer', syncId);
      return RemoteApplyResult.applied;
    }
    final existing = await db.query('AddDealer', columns: ['Did'], where: 'SyncId = ?', whereArgs: [syncId], limit: 1);
    if (existing.isNotEmpty) {
      // Never overwrite DD/D from absolute remote values — children + balance_ops own aggregates.
      await _upsertLocal(db, 'AddDealer', syncId, {
        'DealerName': _asStr(r['dealer_name']),
        'Date': _asStr(r['date_text']),
        'SyncId': syncId,
        'UpdatedAt': updated,
        'SyncDirty': 0,
      }, remoteRow: r);
      return RemoteApplyResult.applied;
    }
    await _upsertLocal(db, 'AddDealer', syncId, {
      'DealerName': _asStr(r['dealer_name']),
      'DDAmount': 0,
      'DAmount': 0,
      'Date': _asStr(r['date_text']),
      'SyncId': syncId,
      'UpdatedAt': updated,
      'SyncDirty': 0,
    }, remoteRow: r);
    return RemoteApplyResult.applied;
  }

  Future<RemoteApplyResult> _applyPetrol(DatabaseExecutor db, Map<String, dynamic> r) async {
    final syncId = r['sync_id']?.toString() ?? '';
    if (syncId.isEmpty) return RemoteApplyResult.safelyAlreadyHandled;
    final updated = r['updated_at']?.toString() ?? SyncMeta.nowIso();
    final deleted = _isDeleted(r['deleted_at']);
    final device = r['device_id']?.toString();
    final gate = await _gateRemoteApply(
      db,
      'PetrolAdd',
      'zaib_petrol_entries',
      syncId,
      updated,
      r,
      remoteDeleted: deleted,
      remoteDeviceId: device,
    );
    if (gate != null) return gate;
    if (deleted) {
      await _applyRemoteDelete(db, 'PetrolAdd', syncId);
      return RemoteApplyResult.applied;
    }
    final custSync = r['customer_sync_id']?.toString();
    final custId = await _localIdBySync(db, 'AddCustomer', 'id', custSync);
    if (custSync != null && custSync.isNotEmpty && custId == null) {
      await _stageRemote(db, 'zaib_petrol_entries', r);
      return RemoteApplyResult.stagedForRetry;
    }
    await _upsertLocal(db, 'PetrolAdd', syncId, {
      'Date': _asStr(r['date_text']),
      'ReceiptNo': _asStr(r['receipt_no']),
      'vehicle': _asStr(r['vehicle']),
      'Litter': _asDouble(r['litter']),
      'Rate': _asDouble(r['rate']),
      'Advance': _asDouble(r['advance']),
      'Amount': _asDouble(r['amount']),
      'Credit': _asDouble(r['credit']),
      'Balance': _asDouble(r['balance']),
      'Note': _asStr(r['note']),
      'CustomerId': custId,
      'Processed': _asInt(r['processed']),
      'IsInitialEntry': _asInt(r['is_initial_entry'], 1),
      'SyncId': syncId,
      'UpdatedAt': updated,
      'SyncDirty': 0,
    }, remoteRow: r);
    return RemoteApplyResult.applied;
  }

  Future<RemoteApplyResult> _applyPayout(DatabaseExecutor db, Map<String, dynamic> r) async {
    final syncId = r['sync_id']?.toString() ?? '';
    if (syncId.isEmpty) return RemoteApplyResult.safelyAlreadyHandled;
    final updated = r['updated_at']?.toString() ?? SyncMeta.nowIso();
    final deleted = _isDeleted(r['deleted_at']);
    final device = r['device_id']?.toString();
    final gate = await _gateRemoteApply(
      db,
      'DieselLedgerCredit',
      'zaib_dealer_payouts',
      syncId,
      updated,
      r,
      remoteDeleted: deleted,
      remoteDeviceId: device,
    );
    if (gate != null) return gate;
    if (deleted) {
      await _applyRemoteDelete(db, 'DieselLedgerCredit', syncId);
      return RemoteApplyResult.applied;
    }
    final dSync = r['dealer_sync_id']?.toString();
    final did = await _localIdBySync(db, 'AddDealer', 'Did', dSync);
    if (dSync != null && dSync.isNotEmpty && did == null) {
      await _stageRemote(db, 'zaib_dealer_payouts', r);
      return RemoteApplyResult.stagedForRetry;
    }
    await _upsertLocal(db, 'DieselLedgerCredit', syncId, {
      'Did': did,
      'Date': _asStr(r['date_text']),
      'AmounGiven': _asDouble(r['amount_given']),
      'Note': _asStr(r['note']),
      'SyncId': syncId,
      'UpdatedAt': updated,
      'SyncDirty': 0,
    }, remoteRow: r);
    await DealerBalanceApply.reconcile(
      db: db,
      sourceSyncId: syncId,
      dealerId: did,
      dealerSyncId: dSync,
      ddDelta: 0,
      dDelta: _asDouble(r['amount_given']),
      markDealerDirty: false,
    );
    return RemoteApplyResult.applied;
  }

  Future<RemoteApplyResult> _applyPurchase(DatabaseExecutor db, Map<String, dynamic> r) async {
    final syncId = r['sync_id']?.toString() ?? '';
    if (syncId.isEmpty) return RemoteApplyResult.safelyAlreadyHandled;
    final updated = r['updated_at']?.toString() ?? SyncMeta.nowIso();
    final deleted = _isDeleted(r['deleted_at']);
    final device = r['device_id']?.toString();
    final gate = await _gateRemoteApply(
      db,
      'AddStock',
      'zaib_dealer_purchases',
      syncId,
      updated,
      r,
      remoteDeleted: deleted,
      remoteDeviceId: device,
    );
    if (gate != null) return gate;
    if (deleted) {
      await _applyRemoteDelete(db, 'AddStock', syncId);
      return RemoteApplyResult.applied;
    }
    final dSync = r['dealer_sync_id']?.toString();
    final did = await _localIdBySync(db, 'AddDealer', 'Did', dSync);
    if (dSync != null && dSync.isNotEmpty && did == null) {
      await _stageRemote(db, 'zaib_dealer_purchases', r);
      return RemoteApplyResult.stagedForRetry;
    }
    await _upsertLocal(db, 'AddStock', syncId, {
      'Vehicle': _asStr(r['vehicle']),
      'Rate': _asDouble(r['rate']),
      'SellDisel': 0,
      'Stock': 0,
      'Date': _asStr(r['date_text']),
      'AddDisel': _asDouble(r['add_diesel']),
      'DealerId': did,
      'Note': _asStr(r['note']),
      'SyncId': syncId,
      'UpdatedAt': updated,
      'SyncDirty': 0,
    }, remoteRow: r);
    final dd = _asDouble(r['add_diesel']) * _asDouble(r['rate']);
    await DealerBalanceApply.reconcile(
      db: db,
      sourceSyncId: syncId,
      dealerId: did,
      dealerSyncId: dSync,
      ddDelta: dd,
      dDelta: 0,
      markDealerDirty: false,
    );
    return RemoteApplyResult.applied;
  }

  Future<RemoteApplyResult> _applyDirect(DatabaseExecutor db, Map<String, dynamic> r) async {
    final syncId = r['sync_id']?.toString() ?? '';
    if (syncId.isEmpty) return RemoteApplyResult.safelyAlreadyHandled;
    final updated = r['updated_at']?.toString() ?? SyncMeta.nowIso();
    final deleted = _isDeleted(r['deleted_at']);
    final device = r['device_id']?.toString();
    final gate = await _gateRemoteApply(
      db,
      'DieselLedgerDebit',
      'zaib_dealer_direct',
      syncId,
      updated,
      r,
      remoteDeleted: deleted,
      remoteDeviceId: device,
    );
    if (gate != null) return gate;
    if (deleted) {
      await _applyRemoteDelete(db, 'DieselLedgerDebit', syncId);
      return RemoteApplyResult.applied;
    }
    final dSync = r['dealer_sync_id']?.toString();
    final did = await _localIdBySync(db, 'AddDealer', 'Did', dSync);
    if (dSync != null && dSync.isNotEmpty && did == null) {
      await _stageRemote(db, 'zaib_dealer_direct', r);
      return RemoteApplyResult.stagedForRetry;
    }
    await _upsertLocal(db, 'DieselLedgerDebit', syncId, {
      'Did': did,
      'Date': _asStr(r['date_text']),
      'AmounGiven': _asDouble(r['amount_given']),
      'Note': _asStr(r['note']),
      'SyncId': syncId,
      'UpdatedAt': updated,
      'SyncDirty': 0,
    }, remoteRow: r);
    await DealerBalanceApply.reconcile(
      db: db,
      sourceSyncId: syncId,
      dealerId: did,
      dealerSyncId: dSync,
      ddDelta: _asDouble(r['amount_given']),
      dDelta: 0,
      markDealerDirty: false,
    );
    return RemoteApplyResult.applied;
  }

  Future<RemoteApplyResult> _applyStock(DatabaseExecutor db, Map<String, dynamic> r) async {
    final syncId = r['sync_id']?.toString() ?? '';
    if (syncId.isEmpty) return RemoteApplyResult.safelyAlreadyHandled;
    final updated = r['updated_at']?.toString() ?? SyncMeta.nowIso();
    final deleted = _isDeleted(r['deleted_at']);
    final device = r['device_id']?.toString();
    final gate = await _gateRemoteApply(
      db,
      'StockDiesel',
      'zaib_stock_diesel',
      syncId,
      updated,
      r,
      remoteDeleted: deleted,
      remoteDeviceId: device,
    );
    if (gate != null) return gate;
    if (deleted) {
      await _applyRemoteDelete(db, 'StockDiesel', syncId);
      return RemoteApplyResult.applied;
    }
    final dSync = r['dealer_sync_id']?.toString();
    final did = await _localIdBySync(db, 'AddDealer', 'Did', dSync);
    if (dSync != null && dSync.isNotEmpty && did == null) {
      await _stageRemote(db, 'zaib_stock_diesel', r);
      return RemoteApplyResult.stagedForRetry;
    }
    await _upsertLocal(db, 'StockDiesel', syncId, {
      'SDid': did,
      'Date': _asStr(r['date_text']),
      'Vehicle': _asStr(r['vehicle']),
      'Litter': _asDouble(r['litter']),
      'Rate': _asDouble(r['rate']),
      'Credit': _asDouble(r['credit']),
      'Debit': _asDouble(r['debit']),
      'Note': _asStr(r['note']),
      'SyncId': syncId,
      'UpdatedAt': updated,
      'SyncDirty': 0,
    }, remoteRow: r);
    return RemoteApplyResult.applied;
  }

  Future<RemoteApplyResult> _applyBank(DatabaseExecutor db, Map<String, dynamic> r) async {
    final syncId = r['sync_id']?.toString() ?? '';
    if (syncId.isEmpty) return RemoteApplyResult.safelyAlreadyHandled;
    final updated = r['updated_at']?.toString() ?? SyncMeta.nowIso();
    final deleted = _isDeleted(r['deleted_at']);
    final device = r['device_id']?.toString();
    final gate = await _gateRemoteApply(
      db,
      'BankTransactions',
      'zaib_bank_transactions',
      syncId,
      updated,
      r,
      remoteDeleted: deleted,
      remoteDeviceId: device,
    );
    if (gate != null) return gate;
    if (deleted) {
      await _applyRemoteDelete(db, 'BankTransactions', syncId);
      return RemoteApplyResult.applied;
    }
    final cSync = r['customer_sync_id']?.toString();
    final dSync = r['dealer_sync_id']?.toString();
    final cid = await _localIdBySync(db, 'AddCustomer', 'id', cSync);
    final did = await _localIdBySync(db, 'AddDealer', 'Did', dSync);
    if ((cSync != null && cSync.isNotEmpty && cid == null) ||
        (dSync != null && dSync.isNotEmpty && did == null)) {
      await _stageRemote(db, 'zaib_bank_transactions', r);
      return RemoteApplyResult.stagedForRetry;
    }
    await _upsertLocal(db, 'BankTransactions', syncId, {
      'TransactionDate': _asStr(r['transaction_date']),
      'TransactionType': _asStr(r['transaction_type']),
      'CustomerId': cid,
      'DealerId': did,
      'Amount': _asDouble(r['amount']),
      'Note': _asStr(r['note']),
      'BankName': _asStr(r['bank_name']),
      'SyncId': syncId,
      'UpdatedAt': updated,
      'SyncDirty': 0,
    }, remoteRow: r);
    return RemoteApplyResult.applied;
  }

  Future<RemoteApplyResult> _applyExpense(DatabaseExecutor db, Map<String, dynamic> r) async {
    final syncId = r['sync_id']?.toString() ?? '';
    if (syncId.isEmpty) return RemoteApplyResult.safelyAlreadyHandled;
    final updated = r['updated_at']?.toString() ?? SyncMeta.nowIso();
    final deleted = _isDeleted(r['deleted_at']);
    final device = r['device_id']?.toString();
    final gate = await _gateRemoteApply(
      db,
      'Expensetable',
      'zaib_expenses',
      syncId,
      updated,
      r,
      remoteDeleted: deleted,
      remoteDeviceId: device,
    );
    if (gate != null) return gate;
    if (deleted) {
      await _applyRemoteDelete(db, 'Expensetable', syncId);
      return RemoteApplyResult.applied;
    }
    await _upsertLocal(db, 'Expensetable', syncId, {
      'Name': _asStr(r['name']),
      'Category': _asStr(r['category']),
      'Amount': _asDouble(r['amount']),
      'EDate': _asStr(r['e_date']),
      'Note': _asStr(r['note']),
      'SyncId': syncId,
      'UpdatedAt': updated,
      'SyncDirty': 0,
    }, remoteRow: r);
    return RemoteApplyResult.applied;
  }

  Future<RemoteApplyResult> _applyBalanceOp(DatabaseExecutor db, Map<String, dynamic> r) async {
    final syncId = r['sync_id']?.toString() ?? '';
    if (syncId.isEmpty) return RemoteApplyResult.safelyAlreadyHandled;
    final updated = r['updated_at']?.toString() ?? SyncMeta.nowIso();
    final deleted = _isDeleted(r['deleted_at']);
    final device = r['device_id']?.toString();
    final gate = await _gateRemoteApply(
      db,
      'SyncDealerBalanceOp',
      'zaib_dealer_balance_ops',
      syncId,
      updated,
      r,
      remoteDeleted: deleted,
      remoteDeviceId: device,
    );
    if (gate != null) return gate;
    final sourceKey = () {
      final s = r['source_sync_id']?.toString();
      if (s != null && s.isNotEmpty) return s;
      return syncId;
    }();
    if (deleted) {
      await DealerBalanceApply.reverse(db: db, sourceSyncId: sourceKey, markDealerDirty: false);
      await db.delete('SyncDealerBalanceOp', where: 'SyncId = ?', whereArgs: [syncId]);
      return RemoteApplyResult.applied;
    }
    final dealerSync = r['dealer_sync_id']?.toString() ?? '';
    final did = await _localIdBySync(db, 'AddDealer', 'Did', dealerSync);
    if (dealerSync.isNotEmpty && did == null) {
      await _stageRemote(db, 'zaib_dealer_balance_ops', r);
      return RemoteApplyResult.stagedForRetry;
    }
    await DealerBalanceApply.reconcile(
      db: db,
      sourceSyncId: sourceKey,
      dealerId: did,
      dealerSyncId: dealerSync,
      ddDelta: _asDouble(r['dd_delta']),
      dDelta: _asDouble(r['d_delta']),
      markDealerDirty: false,
    );
    await db.insert(
      'SyncDealerBalanceOp',
      {
        'SyncId': syncId,
        'DealerSyncId': dealerSync,
        'DdDelta': _asDouble(r['dd_delta']),
        'DDelta': _asDouble(r['d_delta']),
        'SourceKind': _asStr(r['source_kind']).isEmpty ? 'manual' : _asStr(r['source_kind']),
        'SourceSyncId': sourceKey,
        'DateText': _asStr(r['date_text']),
        'Note': _asStr(r['note']),
        'UpdatedAt': updated,
        'SyncDirty': 0,
        'DeletedAt': null,
      },
      conflictAlgorithm: ConflictAlgorithm.replace,
    );
    return RemoteApplyResult.applied;
  }

  Future<RemoteApplyResult> _applyDealerTransfer(DatabaseExecutor db, Map<String, dynamic> r) async {
    final syncId = r['sync_id']?.toString() ?? '';
    if (syncId.isEmpty) return RemoteApplyResult.safelyAlreadyHandled;
    final updated = r['updated_at']?.toString() ?? SyncMeta.nowIso();
    final deleted = _isDeleted(r['deleted_at']);
    final device = r['device_id']?.toString();
    final gate = await _gateRemoteApply(
      db,
      'DealertoDealer',
      'zaib_dealer_transfers',
      syncId,
      updated,
      r,
      remoteDeleted: deleted,
      remoteDeviceId: device,
    );
    if (gate != null) return gate;
    if (deleted) {
      await DealerBalanceApply.reverse(db: db, sourceSyncId: '$syncId:from', markDealerDirty: false);
      await DealerBalanceApply.reverse(db: db, sourceSyncId: '$syncId:to', markDealerDirty: false);
      await _applyRemoteDelete(db, 'DealertoDealer', syncId);
      return RemoteApplyResult.applied;
    }
    final fromSync = r['from_dealer_sync_id']?.toString() ?? r['first_dealer_sync_id']?.toString();
    final toSync = r['to_dealer_sync_id']?.toString() ?? r['second_dealer_sync_id']?.toString();
    final fromDid = await _localIdBySync(db, 'AddDealer', 'Did', fromSync);
    final toDid = await _localIdBySync(db, 'AddDealer', 'Did', toSync);
    if ((fromSync != null && fromSync.isNotEmpty && fromDid == null) ||
        (toSync != null && toSync.isNotEmpty && toDid == null)) {
      await _stageRemote(db, 'zaib_dealer_transfers', r);
      return RemoteApplyResult.stagedForRetry;
    }
    // Cloud column is `amount` (legacy clients may still send amount_given).
    final amount = _asDouble(r['amount'] ?? r['amount_given']);
    await _upsertLocal(db, 'DealertoDealer', syncId, {
      'Date': _asStr(r['date_text']),
      'FirstDealer': fromDid,
      'SecondDealer': toDid,
      'AmounGiven': amount,
      'Note': _asStr(r['note']),
      'SyncId': syncId,
      'UpdatedAt': updated,
      'SyncDirty': 0,
    }, remoteRow: r);
    await DealerBalanceApply.reconcile(
      db: db,
      sourceSyncId: '$syncId:from',
      dealerId: fromDid,
      dealerSyncId: fromSync,
      ddDelta: 0,
      dDelta: amount,
      markDealerDirty: false,
    );
    await DealerBalanceApply.reconcile(
      db: db,
      sourceSyncId: '$syncId:to',
      dealerId: toDid,
      dealerSyncId: toSync,
      ddDelta: amount,
      dDelta: 0,
      markDealerDirty: false,
    );
    return RemoteApplyResult.applied;
  }

  Future<void> _pushDealerTransfers(Database db, String deviceId) async {
    final rows = await db.query('DealertoDealer', where: 'IFNULL(SyncDirty,1) = 1');
    for (final r in rows) {
      final syncId = (r['SyncId']?.toString().isNotEmpty == true) ? r['SyncId'].toString() : SyncMeta.newId();
      final updatedAt = (r['UpdatedAt']?.toString().isNotEmpty == true) ? r['UpdatedAt'].toString() : SyncMeta.nowIso();
      final fromSync = await _syncIdOf(db, 'AddDealer', 'Did', r['FirstDealer']);
      final toSync = await _syncIdOf(db, 'AddDealer', 'Did', r['SecondDealer']);
      if (r['FirstDealer'] != null && (fromSync == null || fromSync.isEmpty)) continue;
      if (r['SecondDealer'] != null && (toSync == null || toSync.isEmpty)) continue;
      try {
        final payload = {
          'sync_id': syncId,
          'local_id': r['LedgerID'],
          'from_dealer_sync_id': fromSync,
          'to_dealer_sync_id': toSync,
          'amount': r['AmounGiven'] ?? 0,
          'date_text': r['Date'] ?? '',
          'note': r['Note'] ?? '',
          'updated_at': updatedAt,
          'deleted_at': null,
          'device_id': deviceId,
        };
        final expectedRev = r['ServerRev'] == null ? null : _asInt(r['ServerRev']);
        final ack = await _upsertAck('zaib_dealer_transfers', payload, expectedServerRev: (expectedRev == null || expectedRev == 0) ? null : expectedRev);
        await _finishUpload(
          db: db,
          cloudTable: 'zaib_dealer_transfers',
          localTable: 'DealertoDealer',
          pkCol: 'LedgerID',
          pk: r['LedgerID']!,
          syncId: syncId,
          uploadedUpdatedAt: updatedAt,
          uploadedRow: payload,
          ack: ack,
        );
      } catch (e) {
        await _logFail('push:zaib_dealer_transfers', syncId, e);
      }
    }
  }
}
