using System;
using System.Collections.Generic;
using System.Data;
using System.Data.SQLite;
using System.IO;
using System.Net.Http;
using System.Net.Http.Headers;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using System.Windows.Forms;
using Newtonsoft.Json;
using Newtonsoft.Json.Linq;
using ZaibPetroleumService.ProjectConnection;

namespace ZaibPetroleumService.Services
{
    /// <summary>
    /// Silent Mobileâ†”PC sync via Supabase schema zaibservice.
    /// Dedup = sync_id. No UI changes.
    /// </summary>
    public static class SupabaseSyncService
    {
        private const string Url = "https://hvcfaslsgewdfblfdsdn.supabase.co";
        private const string AnonKey = "eyJhbGciOiJIUzI1NiIsInR5cCI6IkpXVCJ9.eyJpc3MiOiJzdXBhYmFzZSIsInJlZiI6Imh2Y2Zhc2xzZ2V3ZGZibGZkc2RuIiwicm9sZSI6ImFub24iLCJpYXQiOjE3ODg4ODUxNzcsImV4cCI6MjEwNDQ2MTE3N30.7VXn9EiF1lWZkmW3VVDn36Axx0xO5ypj0oAggHpMXIc";
        private const string Schema = "public";

        private static readonly HttpClient Http = CreateClient();
        private static System.Windows.Forms.Timer _timer;
        private static int _running;
        private static int _queued;
        private static string _deviceId;
        private static readonly Dictionary<string, string> _lastPullByTable =
            new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
        private const string DefaultPull = "1970-01-01T00:00:00.000Z";
        private const int PageSize = 500;
        /// <summary>Protocol v2 checkpoint for zaib_sync_feed.rev (transactional publication counter).</summary>
        private const string ChangeFeedCheckpointKey = "__change_feed_v2__";
        private const string ChangeFeedLegacyCheckpointKey = "__change_feed__";
        private const string ChangeFeedCloudTable = "zaib_sync_feed";

        /// <summary>
        /// Part 6: revisions come from a locked zaib_sync_pub counter in the same transaction as the
        /// business write + feed insert. Rollback undoes the counter bump; concurrent writers serialize.
        /// Do not bootstrap with MAX(id). Legacy __change_feed__ / MAX cursors are repaired by replaying
        /// from rev 0 (LWW + SyncBalanceApplied keep financials idempotent).
        /// </summary>
        private enum RemoteApplyGate { Apply, SkipDone, SkipStage }
        private static long _pullBackoffUntilUtcTicks;
        private static int _noProgressBackoffMs;

        private static HttpClient CreateClient()
        {
            var c = new HttpClient { Timeout = TimeSpan.FromSeconds(60) };
            c.DefaultRequestHeaders.Add("apikey", AnonKey);
            c.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", AnonKey);
            c.DefaultRequestHeaders.Add("Accept-Profile", Schema);
            c.DefaultRequestHeaders.Add("Content-Profile", Schema);
            c.DefaultRequestHeaders.Accept.Add(new MediaTypeWithQualityHeaderValue("application/json"));
            return c;
        }

        public static void StartSilent()
        {
            if (!LocalAppStorage.IsSupabaseSyncEnabled)
                return;
            try
            {
                EnsureLocalSyncReady();
                LoadState();
                if (_timer == null)
                {
                    _timer = new System.Windows.Forms.Timer { Interval = 45000 };
                    _timer.Tick += async (s, e) => await SyncNowSafe();
                    _timer.Start();
                }
                Task.Run(async () => await SyncNowSafe());
            }
            catch
            {
                // silent
            }
        }

        private static string StatePath()
        {
            string folder = LocalAppStorage.GetLocalDataDirectory();
            return Path.Combine(folder, "zaib_sync_state.json");
        }

        private static void LoadState()
        {
            try
            {
                string path = StatePath();
                if (!File.Exists(path)) return;
                var jo = JObject.Parse(File.ReadAllText(path));
                _deviceId = jo.Value<string>("deviceId");
                var legacy = jo.Value<string>("lastPull");
                var byTable = jo["lastPullByTable"] as JObject;
                if (byTable != null)
                {
                    foreach (var p in byTable.Properties())
                        _lastPullByTable[p.Name] = p.Value?.ToString() ?? DefaultPull;
                }
                else if (!string.IsNullOrWhiteSpace(legacy))
                {
                    // Migrate single watermark to per-table checkpoints.
                    foreach (var t in CloudTables())
                        _lastPullByTable[t] = legacy;
                }
            }
            catch { }
            if (string.IsNullOrWhiteSpace(_deviceId))
                _deviceId = Guid.NewGuid().ToString();
        }

        private static string[] CloudTables()
        {
            return new[]
            {
                "zaib_customers","zaib_dealers","zaib_petrol_entries","zaib_dealer_payouts",
                "zaib_dealer_purchases","zaib_dealer_direct","zaib_stock_diesel",
                "zaib_bank_transactions","zaib_expenses","zaib_dealer_balance_ops"
            };
        }

        private static string GetCheckpoint(string cloudTable)
        {
            if (_lastPullByTable.TryGetValue(cloudTable, out var v) && !string.IsNullOrWhiteSpace(v))
            {
                if (!v.Contains("|")) return v + "|";
                return v;
            }
            return DefaultPull + "|";
        }

        private static void SetCheckpoint(string cloudTable, string updatedAt)
        {
            SetCheckpoint(cloudTable, updatedAt, "");
        }

        private static void SetCheckpoint(string cloudTable, string updatedAt, string syncId)
        {
            if (string.IsNullOrWhiteSpace(updatedAt)) return;
            _lastPullByTable[cloudTable] = updatedAt + "|" + (syncId ?? "");
            SaveState();
        }

        private static void SplitCheckpoint(string cursor, out string updatedAt, out string syncId)
        {
            updatedAt = DefaultPull;
            syncId = "";
            if (string.IsNullOrWhiteSpace(cursor)) return;
            int i = cursor.IndexOf('|');
            if (i < 0) { updatedAt = cursor; return; }
            updatedAt = cursor.Substring(0, i);
            syncId = cursor.Substring(i + 1);
        }

        private static void SaveState()
        {
            try
            {
                var by = new JObject();
                foreach (var kv in _lastPullByTable)
                    by[kv.Key] = kv.Value;
                var jo = new JObject
                {
                    ["deviceId"] = _deviceId,
                    ["lastPullByTable"] = by
                };
                File.WriteAllText(StatePath(), jo.ToString(Formatting.None));
            }
            catch { }
        }

        public static void EnsureLocalSyncReady()
        {
            using (var con = new SQLiteConnection(projectconnection.ConnectionString))
            {
                con.Open();
                string[] tables =
                {
                    "AddCustomer","PetrolAdd","AddDealer","DieselLedgerCredit","AddStock",
                    "DieselLedgerDebit","StockDiesel","BankTransactions","Expensetable"
                };
                foreach (var t in tables)
                {
                    DatabaseSchemaManager.EnsureColumn(con, t, "SyncId", "TEXT");
                    DatabaseSchemaManager.EnsureColumn(con, t, "UpdatedAt", "TEXT");
                    DatabaseSchemaManager.EnsureColumn(con, t, "SyncDirty", "INTEGER DEFAULT 1");
                    DatabaseSchemaManager.EnsureColumn(con, t, "ServerRev", "INTEGER");
                }
                if (TableExistsLocal(con, "DealertoDealer"))
                {
                    DatabaseSchemaManager.EnsureColumn(con, "DealertoDealer", "SyncId", "TEXT");
                    DatabaseSchemaManager.EnsureColumn(con, "DealertoDealer", "UpdatedAt", "TEXT");
                    DatabaseSchemaManager.EnsureColumn(con, "DealertoDealer", "SyncDirty", "INTEGER DEFAULT 1");
                    DatabaseSchemaManager.EnsureColumn(con, "DealertoDealer", "ServerRev", "INTEGER");
                    BackfillSyncIds(con, "DealertoDealer", "LedgerID");
                }

                Exec(con, @"CREATE TABLE IF NOT EXISTS SyncTombstone (
                    SyncId TEXT PRIMARY KEY,
                    CloudTable TEXT NOT NULL,
                    DeletedAt TEXT NOT NULL,
                    ExpectedServerRev INTEGER,
                    RequestId TEXT
                );");
                try
                {
                    if (!ColumnExists(con, "SyncTombstone", "ExpectedServerRev"))
                    {
                        TryBackupBeforeTombstoneMigration();
                        DatabaseSchemaManager.EnsureColumn(con, "SyncTombstone", "ExpectedServerRev", "INTEGER");
                        DatabaseSchemaManager.EnsureColumn(con, "SyncTombstone", "RequestId", "TEXT");
                    }
                    else
                    {
                        DatabaseSchemaManager.EnsureColumn(con, "SyncTombstone", "ExpectedServerRev", "INTEGER");
                        DatabaseSchemaManager.EnsureColumn(con, "SyncTombstone", "RequestId", "TEXT");
                    }
                }
                catch { }
                Exec(con, @"CREATE TABLE IF NOT EXISTS SyncApplyGuard (
                    Id INTEGER PRIMARY KEY CHECK (Id = 1)
                );");
                Exec(con, @"CREATE TABLE IF NOT EXISTS SyncFailLog (
                    Id INTEGER PRIMARY KEY AUTOINCREMENT,
                    At TEXT NOT NULL,
                    Scope TEXT NOT NULL,
                    SyncId TEXT,
                    Message TEXT NOT NULL
                );");
                Exec(con, @"CREATE TABLE IF NOT EXISTS SyncStagedRemote (
                    SyncId TEXT NOT NULL,
                    CloudTable TEXT NOT NULL,
                    PayloadJson TEXT NOT NULL,
                    UpdatedAt TEXT NOT NULL,
                    PRIMARY KEY (CloudTable, SyncId)
                );");
                Exec(con, @"CREATE TABLE IF NOT EXISTS SyncRejectedUpload (
                    Id INTEGER PRIMARY KEY AUTOINCREMENT,
                    At TEXT NOT NULL,
                    CloudTable TEXT NOT NULL,
                    SyncId TEXT NOT NULL,
                    LocalUpdatedAt TEXT,
                    PayloadJson TEXT NOT NULL,
                    ServerPayloadJson TEXT,
                    Outcome TEXT NOT NULL
                );");
                Exec(con, @"CREATE TABLE IF NOT EXISTS SyncBalanceApplied (
                    SourceSyncId TEXT PRIMARY KEY,
                    DealerId INTEGER,
                    DealerSyncId TEXT,
                    DdDelta REAL NOT NULL DEFAULT 0,
                    DDelta REAL NOT NULL DEFAULT 0,
                    AppliedAt TEXT NOT NULL
                );");
                Exec(con, @"CREATE TABLE IF NOT EXISTS SyncDealerBalanceOp (
                    SyncId TEXT PRIMARY KEY,
                    DealerSyncId TEXT NOT NULL,
                    DdDelta REAL NOT NULL DEFAULT 0,
                    DDelta REAL NOT NULL DEFAULT 0,
                    SourceKind TEXT NOT NULL DEFAULT 'manual',
                    SourceSyncId TEXT,
                    DateText TEXT,
                    Note TEXT,
                    UpdatedAt TEXT NOT NULL,
                    SyncDirty INTEGER NOT NULL DEFAULT 1,
                    DeletedAt TEXT,
                    ServerRev INTEGER
                );");
                try { DatabaseSchemaManager.EnsureColumn(con, "SyncDealerBalanceOp", "ServerRev", "INTEGER"); } catch { }
                Exec(con, @"CREATE TABLE IF NOT EXISTS SyncCheckpoint (
                    CloudTable TEXT PRIMARY KEY,
                    ChangeFeedCursor INTEGER NOT NULL DEFAULT 0
                );");

                BackfillSyncIds(con, "AddCustomer", "id");
                BackfillSyncIds(con, "PetrolAdd", "pid");
                BackfillSyncIds(con, "AddDealer", "Did");
                BackfillSyncIds(con, "DieselLedgerCredit", "LedgerID");
                BackfillSyncIds(con, "AddStock", "Sid");
                BackfillSyncIds(con, "DieselLedgerDebit", "LedgerID");
                BackfillSyncIds(con, "StockDiesel", "SID");
                BackfillSyncIds(con, "BankTransactions", "Id");
                BackfillSyncIds(con, "Expensetable", "sid");
                CollapseHyphenSyncClones(con);

                // Mark dirty on business-column changes only (avoids sync loop)
                EnsureTrigger(con, "trg_sync_AddCustomer_ai", "AddCustomer", "INSERT", "Name,Mobile,Date");
                EnsureTrigger(con, "trg_sync_AddCustomer_au", "AddCustomer", "UPDATE", "Name,Mobile,Date");
                EnsureTrigger(con, "trg_sync_PetrolAdd_ai", "PetrolAdd", "INSERT", "Date,ReceiptNo,vehicle,Litter,Rate,Advance,Amount,Credit,Balance,Note,CustomerId,Processed,IsInitialEntry");
                EnsureTrigger(con, "trg_sync_PetrolAdd_au", "PetrolAdd", "UPDATE", "Date,ReceiptNo,vehicle,Litter,Rate,Advance,Amount,Credit,Balance,Note,CustomerId,Processed,IsInitialEntry");
                EnsureTrigger(con, "trg_sync_AddDealer_ai", "AddDealer", "INSERT", "DealerName,DDAmount,DAmount,Date");
                EnsureTrigger(con, "trg_sync_AddDealer_au", "AddDealer", "UPDATE", "DealerName,Date");
                BackfillBalanceMarkers(con);
                EnsureTrigger(con, "trg_sync_DieselLedgerCredit_ai", "DieselLedgerCredit", "INSERT", "Did,Date,AmounGiven,Note");
                EnsureTrigger(con, "trg_sync_DieselLedgerCredit_au", "DieselLedgerCredit", "UPDATE", "Did,Date,AmounGiven,Note");
                EnsureTrigger(con, "trg_sync_AddStock_ai", "AddStock", "INSERT", "Vehicle,Rate,SellDisel,Stock,Date,AddDisel,DealerId,Note");
                EnsureTrigger(con, "trg_sync_AddStock_au", "AddStock", "UPDATE", "Vehicle,Rate,SellDisel,Stock,Date,AddDisel,DealerId,Note");
                EnsureTrigger(con, "trg_sync_DieselLedgerDebit_ai", "DieselLedgerDebit", "INSERT", "Did,Date,AmounGiven,Note");
                EnsureTrigger(con, "trg_sync_DieselLedgerDebit_au", "DieselLedgerDebit", "UPDATE", "Did,Date,AmounGiven,Note");
                EnsureTrigger(con, "trg_sync_StockDiesel_ai", "StockDiesel", "INSERT", "SDid,Date,Vehicle,Litter,Rate,Credit,Debit,Note");
                EnsureTrigger(con, "trg_sync_StockDiesel_au", "StockDiesel", "UPDATE", "SDid,Date,Vehicle,Litter,Rate,Credit,Debit,Note");
                EnsureTrigger(con, "trg_sync_BankTransactions_ai", "BankTransactions", "INSERT", "TransactionDate,TransactionType,CustomerId,DealerId,Amount,Note,BankName");
                EnsureTrigger(con, "trg_sync_BankTransactions_au", "BankTransactions", "UPDATE", "TransactionDate,TransactionType,CustomerId,DealerId,Amount,Note,BankName");
                EnsureTrigger(con, "trg_sync_Expensetable_ai", "Expensetable", "INSERT", "Name,Category,Amount,EDate,Note");
                EnsureTrigger(con, "trg_sync_Expensetable_au", "Expensetable", "UPDATE", "Name,Category,Amount,EDate,Note");
                if (TableExistsLocal(con, "DealertoDealer"))
                {
                    EnsureTrigger(con, "trg_sync_DealertoDealer_ai", "DealertoDealer", "INSERT", "Date,FirstDealer,SecondDealer,AmounGiven,Note,id,Did");
                    EnsureTrigger(con, "trg_sync_DealertoDealer_au", "DealertoDealer", "UPDATE", "Date,FirstDealer,SecondDealer,AmounGiven,Note,id,Did");
                }

                // Legacy AFTER DELETE triggers INSERT OR REPLACE SyncTombstone without
                // ExpectedServerRev/RequestId and wipe OCC fields written by DeleteWithTombstone.
                // Soft-delete then fails push → cloud stays live → balances diverge after sync.
                DropLegacyDeleteTombstoneTriggers(con);

                // Stale guard from crash/older builds must not permanently disable dirty tracking.
                // Never mass-mark rows dirty — that would overwrite cloud on next push.
                ClearStaleRemoteApplyGuard(con);
            }
        }

        /// <summary>
        /// Remove old trg_sync_*_ad bodies that clobber OCC tombstone columns.
        /// Deletes must go through LocalPersistence.EnsureTombstone / DeleteWithTombstone only.
        /// </summary>
        private static void DropLegacyDeleteTombstoneTriggers(SQLiteConnection con)
        {
            string[] names =
            {
                "trg_sync_AddCustomer_ad", "trg_sync_PetrolAdd_ad", "trg_sync_AddDealer_ad",
                "trg_sync_DieselLedgerCredit_ad", "trg_sync_AddStock_ad", "trg_sync_DieselLedgerDebit_ad",
                "trg_sync_StockDiesel_ad", "trg_sync_BankTransactions_ad", "trg_sync_Expensetable_ad",
                "trg_sync_DealertoDealer_ad"
            };
            foreach (var name in names)
            {
                try { Exec(con, "DROP TRIGGER IF EXISTS " + name); } catch { }
            }
        }

        private static void EnsureTrigger(SQLiteConnection con, string name, string table, string evt, string cols)
        {
            string pk = PrimaryKey(table);
            string ofCols = evt == "UPDATE" ? (" OF " + cols) : "";
            // DROP+CREATE so WHEN guard is applied (IF NOT EXISTS would keep old body).
            Exec(con, $"DROP TRIGGER IF EXISTS {name}");
            string sql = $@"
CREATE TRIGGER {name}
AFTER {evt}{ofCols} ON {table}
WHEN (SELECT COUNT(*) FROM SyncApplyGuard) = 0
BEGIN
  UPDATE {table}
  SET SyncDirty = 1,
      UpdatedAt = strftime('%Y-%m-%dT%H:%M:%fZ','now'),
      SyncId = CASE WHEN SyncId IS NULL OR SyncId = '' THEN lower(hex(randomblob(16))) ELSE SyncId END
  WHERE {pk} = NEW.{pk};
END;";
            Exec(con, sql);
        }

        /// <summary>
        /// Suppress dirty triggers only while remote rows are written locally.
        /// Must stay inside the same short SQLite write transaction — never across network awaits.
        /// </summary>
        private static void BeginRemoteApply(SQLiteConnection con)
        {
            try { Exec(con, "INSERT OR IGNORE INTO SyncApplyGuard(Id) VALUES(1)"); } catch { }
        }

        private static void EndRemoteApply(SQLiteConnection con)
        {
            try { Exec(con, "DELETE FROM SyncApplyGuard"); } catch { }
        }

        /// <summary>
        /// Older builds left SyncApplyGuard(1) while awaiting HTTP. Clear it safely without
        /// marking every row SyncDirty=1 (that would force-upload and overwrite cloud).
        /// </summary>
        private static void ClearStaleRemoteApplyGuard(SQLiteConnection con)
        {
            try
            {
                using (var cmd = new SQLiteCommand("SELECT COUNT(*) FROM SyncApplyGuard", con))
                {
                    var n = Convert.ToInt32(cmd.ExecuteScalar() ?? 0);
                    if (n <= 0) return;
                }
                EndRemoteApply(con);
                try
                {
                    LogFail(con, "syncApplyGuard", null,
                        new Exception("Cleared stale SyncApplyGuard without mass-dirty (concurrent edits resume tracking)."));
                }
                catch { }
            }
            catch { }
        }

        private static void LogFail(SQLiteConnection con, string scope, string syncId, Exception ex)
        {
            try
            {
                string msg = (ex?.Message ?? "error");
                if (msg.Length > 500) msg = msg.Substring(0, 500);
                msg = System.Text.RegularExpressions.Regex.Replace(msg, @"eyJ[a-zA-Z0-9_-]+\.[a-zA-Z0-9_-]+\.[a-zA-Z0-9_-]+", "[redacted-jwt]");
                using (var cmd = new SQLiteCommand(
                    "INSERT INTO SyncFailLog(At,Scope,SyncId,Message) VALUES(@a,@s,@i,@m)", con))
                {
                    cmd.Parameters.AddWithValue("@a", DateTime.UtcNow.ToString("o"));
                    cmd.Parameters.AddWithValue("@s", scope ?? "");
                    cmd.Parameters.AddWithValue("@i", (object)syncId ?? DBNull.Value);
                    cmd.Parameters.AddWithValue("@m", msg);
                    cmd.ExecuteNonQuery();
                }
            }
            catch { }
        }

        private static string PrimaryKey(string table)
        {
            switch (table)
            {
                case "AddCustomer": return "id";
                case "PetrolAdd": return "pid";
                case "AddDealer": return "Did";
                case "DieselLedgerCredit": return "LedgerID";
                case "AddStock": return "Sid";
                case "DieselLedgerDebit": return "LedgerID";
                case "StockDiesel": return "SID";
                case "BankTransactions": return "Id";
                case "Expensetable": return "sid";
                case "DealertoDealer": return "LedgerID";
                default: return "rowid";
            }
        }

        private static void BackfillSyncIds(SQLiteConnection con, string table, string pk)
        {
            if (!TableExists(con, table)) return;
            using (var cmd = new SQLiteCommand($"SELECT {pk} FROM {table} WHERE SyncId IS NULL OR SyncId = ''", con))
            using (var r = cmd.ExecuteReader())
            {
                var ids = new List<object>();
                while (r.Read()) ids.Add(r[0]);
                r.Close();
                foreach (var id in ids)
                {
                    using (var u = new SQLiteCommand(
                        $"UPDATE {table} SET SyncId=@s, UpdatedAt=@t, SyncDirty=1 WHERE {pk}=@id", con))
                    {
                        u.Parameters.AddWithValue("@s", Guid.NewGuid().ToString());
                        u.Parameters.AddWithValue("@t", DateTime.UtcNow.ToString("o"));
                        u.Parameters.AddWithValue("@id", id);
                        u.ExecuteNonQuery();
                    }
                }
            }
        }

        private static bool TableExists(SQLiteConnection con, string table)
        {
            using (var cmd = new SQLiteCommand("SELECT 1 FROM sqlite_master WHERE type='table' AND name=@n LIMIT 1", con))
            {
                cmd.Parameters.AddWithValue("@n", table);
                return cmd.ExecuteScalar() != null;
            }
        }

        private static bool TableExistsLocal(SQLiteConnection con, string tableName)
        {
            using (var cmd = new SQLiteCommand(
                "SELECT 1 FROM sqlite_master WHERE type='table' AND name=@n LIMIT 1", con))
            {
                cmd.Parameters.AddWithValue("@n", tableName);
                return cmd.ExecuteScalar() != null;
            }
        }

        private static bool ColumnExists(SQLiteConnection con, string table, string column)
        {
            try
            {
                using (var cmd = new SQLiteCommand($"PRAGMA table_info({table})", con))
                using (var r = cmd.ExecuteReader())
                {
                    while (r.Read())
                    {
                        if (string.Equals(Convert.ToString(r["name"]), column, StringComparison.OrdinalIgnoreCase))
                            return true;
                    }
                }
            }
            catch { }
            return false;
        }

        private static void TryBackupBeforeTombstoneMigration()
        {
            try
            {
                string dir = Path.Combine(LocalAppStorage.GetLocalDataDirectory(), "migrations");
                Directory.CreateDirectory(dir);
                string stamp = DateTime.UtcNow.ToString("yyyyMMdd_HHmmss");
                string dest = Path.Combine(dir, "pre_tombstone_rev_" + stamp + ".db");
                DatabaseBackupHelper.CreateSqliteBackup(dest);
                // Also copy WAL/SHM if present beside live DB (consistent snapshot alongside BackupDatabase).
                string live = DatabaseBackupHelper.GetDatabaseFilePath();
                foreach (var suffix in new[] { "-wal", "-shm" })
                {
                    string src = live + suffix;
                    if (File.Exists(src))
                        File.Copy(src, dest + suffix, true);
                }
            }
            catch { /* never block sync on backup failure */ }
        }

        /// <summary>Conflict-adoption only: bypass SyncDirty + timestamp veto; still respects tombstones.</summary>
        [ThreadStatic]
        private static bool _forceAuthoritativeApply;

        private static void Exec(SQLiteConnection con, string sql)
        {
            using (var cmd = new SQLiteCommand(sql, con))
                cmd.ExecuteNonQuery();
        }

        private static async Task SyncNowSafe()
        {
            if (!LocalAppStorage.IsSupabaseSyncEnabled)
                return;
            if (Interlocked.Exchange(ref _running, 1) == 1)
            {
                Interlocked.Exchange(ref _queued, 1);
                return;
            }
            try
            {
                EnsureLocalSyncReady();
                using (var con = new SQLiteConnection(projectconnection.ConnectionString))
                {
                    con.Open();
                    // Deletes first, then parents→children, then protected pull.
                    // Clear any leftover guard before push so local dirty tracking stays live.
                    ClearStaleRemoteApplyGuard(con);

                    await PushTombstones(con);
                    await PushCustomers(con);
                    await PushDealers(con);
                    await PushDealerBalanceOps(con);
                    await PushPetrol(con);
                    await PushPayouts(con);
                    await PushPurchases(con);
                    await PushDirect(con);
                    await PushStock(con);
                    await PushBank(con);
                    await PushExpenses(con);
                    await PushDealerTransfers(con);

                    await EnsureChangeFeedBootstrappedAsync(con);

                    if (DateTime.UtcNow.Ticks < _pullBackoffUntilUtcTicks)
                    {
                        LogFail(con, "pull:backoff", null, new Exception("skip pass (no-progress backoff)"));
                    }
                    else
                    {
                    // Change feed pull: discovery only — row LWW/updated_at/server_rev remains conflict authority.
                    long changeCursor = GetChangeFeedCursor(con);
                    while (true)
                    {
                        var page = await FetchChangeFeedPageAsync(changeCursor);
                        if (page.Count == 0)
                        {
                            _noProgressBackoffMs = 0;
                            _pullBackoffUntilUtcTicks = 0;
                            break;
                        }

                        long advancedTo = changeCursor;
                        long cursorBefore = changeCursor;
                        using (var tx = con.BeginTransaction())
                        {
                            try
                            {
                                BeginRemoteApply(con);
                                foreach (var change in page)
                                {
                                    long rev = change.Value<long?>("rev") ?? 0;
                                    if (rev <= 0) continue;
                                    if (rev != advancedTo + 1)
                                        break;
                                    if (!ApplySyncChangeRow(con, change))
                                        break;
                                    advancedTo = rev;
                                }
                                FlushStaged(con);
                                EndRemoteApply(con);
                                tx.Commit();
                            }
                            catch
                            {
                                try { EndRemoteApply(con); } catch { }
                                try { tx.Rollback(); } catch { }
                                throw;
                            }
                        }

                        if (advancedTo > changeCursor)
                        {
                            SetChangeFeedCursor(con, advancedTo);
                            changeCursor = advancedTo;
                            _noProgressBackoffMs = 0;
                            _pullBackoffUntilUtcTicks = 0;
                        }
                        else
                        {
                            // Full/partial page with no watermark progress — stop this pass; retry later with backoff.
                            _noProgressBackoffMs = _noProgressBackoffMs <= 0
                                ? 2000
                                : Math.Min(_noProgressBackoffMs * 2, 60000);
                            _pullBackoffUntilUtcTicks = DateTime.UtcNow.AddMilliseconds(_noProgressBackoffMs).Ticks;
                            LogFail(con, "pull:no_progress", null,
                                new Exception("cursor=" + cursorBefore + " page=" + page.Count + " backoffMs=" + _noProgressBackoffMs));
                            break;
                        }

                        if (page.Count < PageSize) break;
                    }
                    }
                }
            }
            catch (Exception ex)
            {
                try
                {
                    using (var con = new SQLiteConnection(projectconnection.ConnectionString))
                    {
                        con.Open();
                        LogFail(con, "syncNow", null, ex);
                    }
                }
                catch { }
            }
            finally
            {
                Interlocked.Exchange(ref _running, 0);
                if (Interlocked.Exchange(ref _queued, 0) == 1)
                    await SyncNowSafe();
            }
        }

        /// <summary>Manual dealer DD/D edit: queue cloud op + idempotency marker (balance already updated locally).</summary>
        public static void EnqueueManualDealerBalanceOp(int dealerId, string dealerSyncId, double ddDelta, double dDelta, string dateText = null)
        {
            EnqueueDealerBalanceOp(dealerId, dealerSyncId, ddDelta, dDelta, dateText, "manual", null);
        }

        /// <summary>New dealer opening DD/D: stable SourceSyncId opening:{dealerSyncId}, source_kind opening.</summary>
        public static void EnqueueOpeningDealerBalanceOp(int dealerId, string dealerSyncId, double ddDelta, double dDelta, string dateText = null)
        {
            if (string.IsNullOrWhiteSpace(dealerSyncId)) return;
            EnqueueDealerBalanceOp(dealerId, dealerSyncId, ddDelta, dDelta, dateText, "opening", "opening:" + dealerSyncId);
        }

        private static void EnqueueDealerBalanceOp(
            int dealerId, string dealerSyncId, double ddDelta, double dDelta, string dateText,
            string sourceKind, string fixedSourceSyncId)
        {
            if (string.IsNullOrWhiteSpace(dealerSyncId) || (Math.Abs(ddDelta) < 1e-9 && Math.Abs(dDelta) < 1e-9))
                return;
            EnsureLocalSyncReady();
            MainClass.RunInTransaction((con, tx) =>
                EnqueueDealerBalanceOpInTx(con, tx, dealerId, dealerSyncId, ddDelta, dDelta, dateText, sourceKind, fixedSourceSyncId));
        }

        /// <summary>Queue balance op + idempotency marker in an open transaction (dealer row already saved).</summary>
        public static bool EnqueueDealerBalanceOpInTx(
            SQLiteConnection con, SQLiteTransaction tx,
            int dealerId, string dealerSyncId, double ddDelta, double dDelta, string dateText,
            string sourceKind, string fixedSourceSyncId)
        {
            if (string.IsNullOrWhiteSpace(dealerSyncId) || (Math.Abs(ddDelta) < 1e-9 && Math.Abs(dDelta) < 1e-9))
                return true;
            string opSyncId = Guid.NewGuid().ToString();
            string sourceSyncId = string.IsNullOrWhiteSpace(fixedSourceSyncId) ? opSyncId : fixedSourceSyncId;
            string kind = string.IsNullOrWhiteSpace(sourceKind) ? "manual" : sourceKind;
            string now = DateTime.UtcNow.ToString("o");
            using (var cmd = new SQLiteCommand(
                @"INSERT INTO SyncDealerBalanceOp(SyncId,DealerSyncId,DdDelta,DDelta,SourceKind,SourceSyncId,DateText,UpdatedAt,SyncDirty,DeletedAt)
                  VALUES(@sid,@ds,@dd,@d,@sk,@src,@dt,@u,1,NULL)", con, tx))
            {
                cmd.Parameters.AddWithValue("@sid", opSyncId);
                cmd.Parameters.AddWithValue("@ds", dealerSyncId);
                cmd.Parameters.AddWithValue("@dd", ddDelta);
                cmd.Parameters.AddWithValue("@d", dDelta);
                cmd.Parameters.AddWithValue("@sk", kind);
                cmd.Parameters.AddWithValue("@src", sourceSyncId);
                cmd.Parameters.AddWithValue("@dt", (object)dateText ?? DBNull.Value);
                cmd.Parameters.AddWithValue("@u", now);
                if (cmd.ExecuteNonQuery() <= 0) return false;
            }
            SetBalanceMarkerOnly(con, tx, sourceSyncId, dealerId, ddDelta, dDelta);
            return true;
        }

        /// <summary>Record that a child row's balance effect was applied locally (does not adjust DD/D).</summary>
        public static void SetBalanceMarkerOnly(
            SQLiteConnection con, SQLiteTransaction tx,
            string sourceSyncId, object dealerId, double ddDelta, double dDelta)
        {
            sourceSyncId = NormalizeSyncId(sourceSyncId);
            if (string.IsNullOrWhiteSpace(sourceSyncId)) return;
            string dealerSync = DealerSyncId(con, dealerId);
            int? did = null;
            if (dealerId != null && dealerId != DBNull.Value)
                did = Convert.ToInt32(dealerId);
            string now = DateTime.UtcNow.ToString("o");
            using (var cmd = new SQLiteCommand(
                @"INSERT OR REPLACE INTO SyncBalanceApplied(SourceSyncId, DealerId, DealerSyncId, DdDelta, DDelta, AppliedAt)
                  VALUES(@src,@did,@ds,@dd,@d,@a)", con, tx))
            {
                cmd.Parameters.AddWithValue("@src", sourceSyncId);
                cmd.Parameters.AddWithValue("@did", (object)did ?? DBNull.Value);
                cmd.Parameters.AddWithValue("@ds", (object)dealerSync ?? DBNull.Value);
                cmd.Parameters.AddWithValue("@dd", ddDelta);
                cmd.Parameters.AddWithValue("@d", dDelta);
                cmd.Parameters.AddWithValue("@a", now);
                cmd.ExecuteNonQuery();
            }
        }

        /// <summary>After local delete reversed DD/D, drop marker so pull does not re-apply.</summary>
        public static void DeleteBalanceMarkerOnly(SQLiteConnection con, SQLiteTransaction tx, string sourceSyncId)
        {
            sourceSyncId = NormalizeSyncId(sourceSyncId);
            if (string.IsNullOrWhiteSpace(sourceSyncId)) return;
            using (var cmd = new SQLiteCommand("DELETE FROM SyncBalanceApplied WHERE SourceSyncId=@s", con, tx))
            {
                cmd.Parameters.AddWithValue("@s", sourceSyncId);
                cmd.ExecuteNonQuery();
            }
        }

        /// <summary>Wire forms: marker only after local balance already updated in same transaction.</summary>
        public static void RecordLocalChildBalanceEffect(
            SQLiteConnection con, SQLiteTransaction tx, string localTable, string pkCol, object pk)
        {
            DataRow row = LocalPersistence.ReadRow(localTable, pkCol, pk, con, tx);
            if (row == null) return;
            string syncId = row.Table.Columns.Contains("SyncId") ? Convert.ToString(row["SyncId"]) : null;
            if (string.IsNullOrWhiteSpace(syncId)) return;
            switch (localTable)
            {
                case "DieselLedgerCredit":
                    SetBalanceMarkerOnly(con, tx, syncId, row["Did"], 0, ToD(row["AmounGiven"]));
                    break;
                case "DieselLedgerDebit":
                    SetBalanceMarkerOnly(con, tx, syncId, row["Did"], ToD(row["AmounGiven"]), 0);
                    break;
                case "AddStock":
                    SetBalanceMarkerOnly(con, tx, syncId, row["DealerId"], ToD(row["AddDisel"]) * ToD(row["Rate"]), 0);
                    break;
            }
        }

        /// <summary>Dealer-to-dealer transfer: idempotent markers for from (D) and to (DD) sides.</summary>
        public static void RecordLocalDealerTransferBalanceEffect(
            SQLiteConnection con, SQLiteTransaction tx, string syncId, object fromDealerId, object toDealerId, double amount)
        {
            if (string.IsNullOrWhiteSpace(syncId)) return;
            SetBalanceMarkerOnly(con, tx, syncId + ":from", fromDealerId, 0, amount);
            SetBalanceMarkerOnly(con, tx, syncId + ":to", toDealerId, amount, 0);
        }

        private enum UploadAck { Accepted, Duplicate, Conflict, Deleted, Failure, Unknown }

        private static async Task<UploadAck> UpsertAsync(string table, object row, long? expectedServerRev = null, string requestIdOverride = null)
        {
            string json = JsonConvert.SerializeObject(row);
            var uploaded = JObject.Parse(json);
            uploaded.Remove("server_rev");
            string upAt = uploaded.Value<string>("updated_at");
            string upDev = uploaded.Value<string>("device_id");
            string syncId = uploaded.Value<string>("sync_id");
            string requestId = !string.IsNullOrWhiteSpace(requestIdOverride)
                ? requestIdOverride
                : StableRequestId(table, syncId, upAt);

            try
            {
                var outcome = await CallSyncApplyAsync(table, uploaded, expectedServerRev, requestId);
                if (outcome == null) return UploadAck.Unknown;

                uploaded["_ack_server_rev"] = outcome["server_rev"];
                uploaded["_ack_row"] = outcome["row"];
                uploaded["_ack_status"] = outcome["status"];
                // Mutate original serialization bag via side channel dictionary
                LastUpsertMeta[syncId ?? ""] = uploaded;

                string status = outcome.Value<string>("status") ?? "";
                switch (status.ToLowerInvariant())
                {
                    case "accepted": return UploadAck.Accepted;
                    case "duplicate": return UploadAck.Duplicate;
                    case "conflict": return UploadAck.Conflict;
                    case "deleted": return UploadAck.Deleted;
                    case "rejected": return UploadAck.Failure;
                    default: return UploadAck.Failure;
                }
            }
            catch (Exception ex)
            {
                try
                {
                    using (var con = new SQLiteConnection(projectconnection.ConnectionString))
                    {
                        con.Open();
                        LogFail(con, "rpc:" + table, syncId, ex);
                    }
                }
                catch { }
                return UploadAck.Unknown;
            }
        }

        private static readonly Dictionary<string, JObject> LastUpsertMeta =
            new Dictionary<string, JObject>(StringComparer.OrdinalIgnoreCase);

        private static string StableRequestId(string table, string syncId, string updatedAt)
        {
            // Deterministic GUID from table|sync|updated for idempotent retries.
            using (var md5 = System.Security.Cryptography.MD5.Create())
            {
                byte[] hash = md5.ComputeHash(Encoding.UTF8.GetBytes(table + "|" + (syncId ?? "") + "|" + (updatedAt ?? "")));
                return new Guid(hash).ToString();
            }
        }

        private static async Task<JObject> CallSyncApplyAsync(string table, JObject payload, long? expectedRev, string requestId)
        {
            var body = new JObject
            {
                ["p_table"] = table,
                ["p_payload"] = payload,
                ["p_expected_rev"] = expectedRev.HasValue ? (JToken)expectedRev.Value : JValue.CreateNull(),
                ["p_request_id"] = requestId,
                ["p_allow_restore"] = false
            };
            var req = new HttpRequestMessage(HttpMethod.Post, $"{Url}/rest/v1/rpc/zaib_sync_apply")
            {
                Content = new StringContent(body.ToString(Formatting.None), Encoding.UTF8, "application/json")
            };
            var res = await Http.SendAsync(req);
            string resp = await res.Content.ReadAsStringAsync();
            if (!res.IsSuccessStatusCode) return null;
            if (string.IsNullOrWhiteSpace(resp)) return null;
            var token = JToken.Parse(resp);
            if (token is JObject jo) return jo;
            if (token is JArray arr && arr.Count > 0 && arr[0] is JObject first) return first;
            return null;
        }

        private static async Task FinishUpload(
            SQLiteConnection con,
            string cloudTable,
            string localTable,
            string pk,
            object id,
            string syncId,
            string uploadedUpdatedAt,
            string uploadedJson,
            UploadAck ack)
        {
            JObject meta = null;
            if (!string.IsNullOrWhiteSpace(syncId) && LastUpsertMeta.TryGetValue(syncId, out var m))
                meta = m;
            // Only stamp ServerRev on accept/duplicate — never advance OCC base on conflict observation.
            if (ack == UploadAck.Accepted || ack == UploadAck.Duplicate)
            {
                long? ackRev = meta?["_ack_server_rev"]?.Value<long?>();
                if (ackRev.HasValue && ackRev.Value > 0)
                    StoreServerRev(con, localTable, pk, id, ackRev.Value);
                MarkCleanIfVersion(con, localTable, pk, id, syncId, uploadedUpdatedAt);
                return;
            }
            if (ack == UploadAck.Failure || ack == UploadAck.Unknown)
            {
                LogFail(con, "upload:" + ack + ":" + cloudTable, syncId,
                    new Exception(ack == UploadAck.Unknown
                        ? "unknown commit outcome — keep dirty; retry same request_id"
                        : "empty/invalid ack or transport failure"));
                return;
            }

            if (ack == UploadAck.Deleted)
            {
                string serverJson = meta?["_ack_row"]?.ToString(Formatting.None);
                try
                {
                    using (var cmd = new SQLiteCommand(
                        @"INSERT INTO SyncRejectedUpload(At,CloudTable,SyncId,LocalUpdatedAt,PayloadJson,ServerPayloadJson,Outcome)
                          VALUES(@a,@t,@s,@u,@p,@sp,@o)", con))
                    {
                        cmd.Parameters.AddWithValue("@a", DateTime.UtcNow.ToString("o"));
                        cmd.Parameters.AddWithValue("@t", cloudTable);
                        cmd.Parameters.AddWithValue("@s", syncId);
                        cmd.Parameters.AddWithValue("@u", (object)uploadedUpdatedAt ?? DBNull.Value);
                        cmd.Parameters.AddWithValue("@p", uploadedJson ?? "{}");
                        cmd.Parameters.AddWithValue("@sp", (object)serverJson ?? DBNull.Value);
                        cmd.Parameters.AddWithValue("@o", "serverDeleted");
                        cmd.ExecuteNonQuery();
                    }
                }
                catch { }
                if (meta?["_ack_row"] is JObject joDel)
                    StageRemote(con, cloudTable, joDel);
                return;
            }

            string serverJson2 = null;
            JObject serverJo = null;
            try
            {
                if (meta?["_ack_row"] is JObject joAck)
                {
                    serverJo = joAck;
                    serverJson2 = joAck.ToString(Formatting.None);
                    StageRemote(con, cloudTable, joAck);
                }
                else
                {
                    var res = await Http.GetAsync($"{Url}/rest/v1/{cloudTable}?sync_id=eq.{Uri.EscapeDataString(syncId)}&select=*&limit=1");
                    if (res.IsSuccessStatusCode)
                    {
                        var body = await res.Content.ReadAsStringAsync();
                        var arr = JArray.Parse(string.IsNullOrWhiteSpace(body) ? "[]" : body);
                        if (arr.Count > 0 && arr[0] is JObject jo)
                        {
                            serverJo = jo;
                            serverJson2 = jo.ToString(Formatting.None);
                            StageRemote(con, cloudTable, jo);
                        }
                    }
                }
            }
            catch (Exception ex) { LogFail(con, "upload:conflict-fetch:" + cloudTable, syncId, ex); }

            string localNow = null;
            using (var cmd = new SQLiteCommand($"SELECT UpdatedAt FROM {localTable} WHERE {pk}=@id LIMIT 1", con))
            {
                cmd.Parameters.AddWithValue("@id", id);
                var v = cmd.ExecuteScalar();
                localNow = v == null || v == DBNull.Value ? null : Convert.ToString(v);
            }
            bool sameVersion = !string.IsNullOrWhiteSpace(localNow) && !string.IsNullOrWhiteSpace(uploadedUpdatedAt)
                && string.Equals(localNow, uploadedUpdatedAt, StringComparison.Ordinal);
            bool serverDeleted = serverJo != null
                && serverJo["deleted_at"] != null && serverJo["deleted_at"].Type != JTokenType.Null;
            if (HasTombstone(con, syncId) && !serverDeleted)
                sameVersion = false; // preserve pending delete over live adopt
            string outcome = sameVersion ? "adoptServerClearDirty" : "keepLocalDirtyStageServer";
            try
            {
                using (var cmd = new SQLiteCommand(
                    @"INSERT INTO SyncRejectedUpload(At,CloudTable,SyncId,LocalUpdatedAt,PayloadJson,ServerPayloadJson,Outcome)
                      VALUES(@a,@t,@s,@u,@p,@sp,@o)", con))
                {
                    cmd.Parameters.AddWithValue("@a", DateTime.UtcNow.ToString("o"));
                    cmd.Parameters.AddWithValue("@t", cloudTable);
                    cmd.Parameters.AddWithValue("@s", syncId);
                    cmd.Parameters.AddWithValue("@u", (object)uploadedUpdatedAt ?? DBNull.Value);
                    cmd.Parameters.AddWithValue("@p", uploadedJson ?? "{}");
                    cmd.Parameters.AddWithValue("@sp", (object)serverJson2 ?? DBNull.Value);
                    cmd.Parameters.AddWithValue("@o", outcome);
                    cmd.ExecuteNonQuery();
                }
            }
            catch { }

            if (sameVersion && serverJo != null)
            {
                // Atomic adopt: apply authoritative values/deletion + balances + ServerRev + dirty + clear staged.
                using (var tx = con.BeginTransaction())
                {
                    try
                    {
                        string stillAt = null;
                        using (var cmd = new SQLiteCommand($"SELECT UpdatedAt FROM {localTable} WHERE {pk}=@id LIMIT 1", con, tx))
                        {
                            cmd.Parameters.AddWithValue("@id", id);
                            var v = cmd.ExecuteScalar();
                            stillAt = v == null || v == DBNull.Value ? null : Convert.ToString(v);
                        }
                        if (!string.IsNullOrWhiteSpace(stillAt)
                            && !string.Equals(stillAt, uploadedUpdatedAt, StringComparison.Ordinal))
                        {
                            tx.Rollback();
                            return; // mid-upload newer edit
                        }
                        if (HasTombstone(con, syncId) && !serverDeleted)
                        {
                            tx.Rollback();
                            return;
                        }

                        bool prev = _forceAuthoritativeApply;
                        _forceAuthoritativeApply = true;
                        try
                        {
                            bool applied = ApplySyncChangeRow(con, new JObject
                            {
                                ["cloud_table"] = cloudTable,
                                ["row_sync_id"] = syncId,
                                ["op"] = serverDeleted ? "delete" : "upsert",
                                ["payload"] = serverJo
                            });
                            if (!applied)
                                throw new InvalidOperationException("adoption blocked (missing parent) — retain dirty");
                        }
                        finally
                        {
                            _forceAuthoritativeApply = prev;
                        }

                        using (var cmd = new SQLiteCommand(
                            "DELETE FROM SyncStagedRemote WHERE CloudTable=@t AND SyncId=@s", con, tx))
                        {
                            cmd.Parameters.AddWithValue("@t", cloudTable);
                            cmd.Parameters.AddWithValue("@s", syncId);
                            cmd.ExecuteNonQuery();
                        }
                        tx.Commit();
                    }
                    catch (Exception ex)
                    {
                        try { tx.Rollback(); } catch { }
                        LogFail(con, "upload:adopt-rollback:" + cloudTable, syncId, ex);
                    }
                }
            }
            // Mid-upload edit: keep dirty and keep prior ServerRev base; staged remote for later.
        }

        private static void StoreServerRev(SQLiteConnection con, string localTable, string pk, object id, long rev)
        {
            if (rev <= 0 || id == null || id == DBNull.Value) return;
            try
            {
                using (var cmd = new SQLiteCommand($"UPDATE {localTable} SET ServerRev=@r WHERE {pk}=@id", con))
                {
                    cmd.Parameters.AddWithValue("@r", rev);
                    cmd.Parameters.AddWithValue("@id", id);
                    cmd.ExecuteNonQuery();
                }
            }
            catch { }
        }

        private static long? ReadServerRev(SQLiteConnection con, string localTable, string pk, object id)
        {
            try
            {
                using (var cmd = new SQLiteCommand($"SELECT ServerRev FROM {localTable} WHERE {pk}=@id LIMIT 1", con))
                {
                    cmd.Parameters.AddWithValue("@id", id);
                    var v = cmd.ExecuteScalar();
                    if (v == null || v == DBNull.Value) return null;
                    long n = Convert.ToInt64(v);
                    return n <= 0 ? (long?)null : n;
                }
            }
            catch { return null; }
        }

        private static long GetChangeFeedCursor(SQLiteConnection con)
        {
            using (var cmd = new SQLiteCommand(
                "SELECT ChangeFeedCursor FROM SyncCheckpoint WHERE CloudTable=@t LIMIT 1", con))
            {
                cmd.Parameters.AddWithValue("@t", ChangeFeedCheckpointKey);
                var v = cmd.ExecuteScalar();
                if (v == null || v == DBNull.Value) return 0;
                return Convert.ToInt64(v);
            }
        }

        private static void SetChangeFeedCursor(SQLiteConnection con, long cursor)
        {
            using (var cmd = new SQLiteCommand(
                @"INSERT INTO SyncCheckpoint(CloudTable, ChangeFeedCursor) VALUES(@t,@c)
                  ON CONFLICT(CloudTable) DO UPDATE SET ChangeFeedCursor=@c", con))
            {
                cmd.Parameters.AddWithValue("@t", ChangeFeedCheckpointKey);
                cmd.Parameters.AddWithValue("@c", cursor);
                cmd.ExecuteNonQuery();
            }
        }

        private static bool HasChangeFeedCheckpoint(SQLiteConnection con)
        {
            using (var cmd = new SQLiteCommand(
                "SELECT 1 FROM SyncCheckpoint WHERE CloudTable=@t LIMIT 1", con))
            {
                cmd.Parameters.AddWithValue("@t", ChangeFeedCheckpointKey);
                return cmd.ExecuteScalar() != null;
            }
        }

        private static bool HasLegacyPerTablePullCheckpoints()
        {
            foreach (var kv in _lastPullByTable)
            {
                SplitCheckpoint(kv.Value, out var at, out _);
                if (!string.Equals(at, DefaultPull, StringComparison.Ordinal))
                    return true;
            }
            return false;
        }

        private static bool LocalHasSyncedBusinessRows(SQLiteConnection con)
        {
            string[] probes =
            {
                "SELECT 1 FROM AddCustomer WHERE SyncId IS NOT NULL AND trim(SyncId)<>'' AND IFNULL(SyncDirty,1)=0 LIMIT 1",
                "SELECT 1 FROM AddDealer WHERE SyncId IS NOT NULL AND trim(SyncId)<>'' AND IFNULL(SyncDirty,1)=0 LIMIT 1",
                "SELECT 1 FROM PetrolAdd WHERE SyncId IS NOT NULL AND trim(SyncId)<>'' AND IFNULL(SyncDirty,1)=0 LIMIT 1",
                "SELECT 1 FROM DealertoDealer WHERE SyncId IS NOT NULL AND trim(SyncId)<>'' AND IFNULL(SyncDirty,1)=0 LIMIT 1"
            };
            foreach (var sql in probes)
            {
                try
                {
                    using (var cmd = new SQLiteCommand(sql, con))
                        if (cmd.ExecuteScalar() != null) return true;
                }
                catch { }
            }
            return false;
        }

        private static async Task EnsureChangeFeedBootstrappedAsync(SQLiteConnection con)
        {
            if (HasChangeFeedCheckpoint(con)) return;

            // Protocol v2: never MAX-skip. Replay from rev 0 (resumable via cursor advances).
            // Legacy __change_feed__ MAX bootstrap is intentionally ignored so missed history is repaired.
            if (HasLegacyChangeFeedCheckpoint(con))
                LogFail(con, "pull:bootstrap_repair", null, new Exception("legacy MAX cursor → chg_v2:0"));

            SetChangeFeedCursor(con, 0);
            await Task.CompletedTask;
        }

        private static bool HasLegacyChangeFeedCheckpoint(SQLiteConnection con)
        {
            using (var cmd = new SQLiteCommand(
                "SELECT 1 FROM SyncCheckpoint WHERE CloudTable=@t LIMIT 1", con))
            {
                cmd.Parameters.AddWithValue("@t", ChangeFeedLegacyCheckpointKey);
                return cmd.ExecuteScalar() != null;
            }
        }

        private static async Task<List<JObject>> FetchChangeFeedPageAsync(long cursor)
        {
            string url =
                $"{Url}/rest/v1/{ChangeFeedCloudTable}?rev=gt.{cursor}" +
                $"&order=rev.asc&limit={PageSize}";
            var res = await Http.GetAsync(url);
            res.EnsureSuccessStatusCode();
            string body = await res.Content.ReadAsStringAsync();
            var arr = JArray.Parse(string.IsNullOrWhiteSpace(body) ? "[]" : body);
            var list = new List<JObject>();
            foreach (var t in arr)
                if (t is JObject jo) list.Add(jo);
            return list;
        }

        private static RemoteApplyGate ClassifyRemoteApply(
            SQLiteConnection con, string localTable, string syncId, string updated, bool deleted, string remoteDeviceId,
            long? remoteServerRev = null)
        {
            if (HasTombstone(con, syncId))
                return deleted ? RemoteApplyGate.Apply : RemoteApplyGate.SkipStage;
            if (!_forceAuthoritativeApply && IsSyncDirty(con, localTable, syncId))
                return RemoteApplyGate.SkipStage;
            if (_forceAuthoritativeApply)
                return RemoteApplyGate.Apply;
            if (ShouldApply(con, localTable, syncId, updated, deleted, remoteDeviceId, remoteServerRev))
                return RemoteApplyGate.Apply;
            return RemoteApplyGate.SkipDone;
        }

        private static void RequireApplyRows(int rows, string scope, string syncId)
        {
            if (rows <= 0)
                throw new InvalidOperationException($"Sync apply failed ({scope}) syncId={syncId ?? ""}");
        }

        /// <summary>Apply one zaib_sync_feed row. False stalls contiguous rev watermark.</summary>
        private static bool ApplySyncChangeRow(SQLiteConnection con, JObject change)
        {
            string cloudTable = change.Value<string>("cloud_table");
            string rowSyncId = change.Value<string>("row_sync_id");
            string op = change.Value<string>("op");
            if (string.IsNullOrWhiteSpace(cloudTable) || string.IsNullOrWhiteSpace(rowSyncId))
                return true;

            JObject payload = change["payload"] as JObject ?? new JObject();
            if (string.IsNullOrWhiteSpace(payload.Value<string>("sync_id")))
                payload["sync_id"] = rowSyncId;

            bool deleted = string.Equals(op, "delete", StringComparison.OrdinalIgnoreCase)
                || (payload["deleted_at"] != null && payload["deleted_at"].Type != JTokenType.Null);

            switch (cloudTable)
            {
                case "zaib_customers":
                    return ApplyOneCustomer(con, payload, deleted);
                case "zaib_dealers":
                    return ApplyOneDealer(con, payload, deleted);
                case "zaib_petrol_entries":
                    return ApplyOnePetrol(con, payload, deleted);
                case "zaib_dealer_payouts":
                    return UpsertChild(con, "DieselLedgerCredit", "LedgerID", cloudTable, payload,
                        (cmd, row, did) =>
                        {
                            cmd.Parameters.AddWithValue("@Did", did);
                            cmd.Parameters.AddWithValue("@Date", row.Value<string>("date_text") ?? "");
                            cmd.Parameters.AddWithValue("@AmounGiven", row.Value<double?>("amount_given") ?? 0);
                            cmd.Parameters.AddWithValue("@Note", row.Value<string>("note") ?? "");
                        },
                        "Did,Date,AmounGiven,Note", "AddDealer", "Did", "dealer_sync_id");
                case "zaib_dealer_purchases":
                    return ApplyOnePurchase(con, payload, deleted);
                case "zaib_dealer_direct":
                    return UpsertChild(con, "DieselLedgerDebit", "LedgerID", cloudTable, payload,
                        (cmd, row, did) =>
                        {
                            cmd.Parameters.AddWithValue("@Did", did);
                            cmd.Parameters.AddWithValue("@Date", row.Value<string>("date_text") ?? "");
                            cmd.Parameters.AddWithValue("@AmounGiven", row.Value<double?>("amount_given") ?? 0);
                            cmd.Parameters.AddWithValue("@Note", row.Value<string>("note") ?? "");
                        },
                        "Did,Date,AmounGiven,Note", "AddDealer", "Did", "dealer_sync_id");
                case "zaib_stock_diesel":
                    return ApplyOneStock(con, payload, deleted);
                case "zaib_bank_transactions":
                    return ApplyOneBank(con, payload, deleted);
                case "zaib_expenses":
                    return ApplyOneExpense(con, payload, deleted);
                case "zaib_dealer_balance_ops":
                    {
                        string updated = payload.Value<string>("updated_at") ?? DateTime.UtcNow.ToString("o");
                        ApplyDealerBalanceOpRow(con, payload, updated, deleted, out bool advanceCp);
                        return advanceCp;
                    }
                case "zaib_dealer_transfers":
                    return ApplyOneDealerTransfer(con, payload, deleted);
                default:
                    LogFail(con, "changeFeed:unknown", rowSyncId, new Exception("unknown cloud_table " + cloudTable));
                    return true;
            }
        }

        private static void PersistObservedServerRev(SQLiteConnection con, string localTable, string syncId, JObject r)
        {
            if (string.IsNullOrWhiteSpace(syncId) || r == null) return;
            long? rev = r.Value<long?>("server_rev");
            if (!rev.HasValue || rev.Value <= 0) return;
            try
            {
                using (var cmd = new SQLiteCommand(
                    $"UPDATE {localTable} SET ServerRev=@r WHERE {WhereSyncIdEq()} AND IFNULL(SyncDirty,0)=0", con))
                {
                    cmd.Parameters.AddWithValue("@r", rev.Value);
                    BindNormSync(cmd, syncId);
                    cmd.ExecuteNonQuery();
                }
            }
            catch { }
        }

        private static bool ApplyOneCustomer(SQLiteConnection con, JObject r, bool deleted)
        {
            string syncId = r.Value<string>("sync_id");
            if (string.IsNullOrWhiteSpace(syncId)) return true;
            string updated = r.Value<string>("updated_at") ?? DateTime.UtcNow.ToString("o");
            var gate = ClassifyRemoteApply(con, "AddCustomer", syncId, updated, deleted, r.Value<string>("device_id"), r.Value<long?>("server_rev"));
            if (gate == RemoteApplyGate.SkipStage)
            {
                StageRemote(con, "zaib_customers", r);
                return false;
            }
            if (gate == RemoteApplyGate.SkipDone) return true;
            if (deleted) { ApplyRemoteDelete(con, "AddCustomer", syncId); return true; }
            using (var chk = new SQLiteCommand("SELECT id FROM AddCustomer WHERE SyncId=@s", con))
            {
                chk.Parameters.AddWithValue("@s", syncId);
                var exists = chk.ExecuteScalar();
                if (exists == null)
                {
                    using (var i = new SQLiteCommand("INSERT INTO AddCustomer(Name,Mobile,Date,SyncId,UpdatedAt,SyncDirty) VALUES(@n,@m,@d,@s,@u,0)", con))
                    {
                        i.Parameters.AddWithValue("@n", r.Value<string>("name") ?? "");
                        i.Parameters.AddWithValue("@m", r.Value<string>("mobile") ?? "");
                        i.Parameters.AddWithValue("@d", r.Value<string>("date_text") ?? "");
                        i.Parameters.AddWithValue("@s", syncId);
                        i.Parameters.AddWithValue("@u", updated);
                        RequireApplyRows(i.ExecuteNonQuery(), "insert:AddCustomer", syncId);
                    }
                }
                else
                {
                    using (var u = new SQLiteCommand("UPDATE AddCustomer SET Name=@n, Mobile=@m, Date=@d, UpdatedAt=@u, SyncDirty=0 WHERE SyncId=@s", con))
                    {
                        u.Parameters.AddWithValue("@n", r.Value<string>("name") ?? "");
                        u.Parameters.AddWithValue("@m", r.Value<string>("mobile") ?? "");
                        u.Parameters.AddWithValue("@d", r.Value<string>("date_text") ?? "");
                        u.Parameters.AddWithValue("@u", updated);
                        u.Parameters.AddWithValue("@s", syncId);
                        RequireApplyRows(u.ExecuteNonQuery(), "update:AddCustomer", syncId);
                    }
                }
            }
            PersistObservedServerRev(con, "AddCustomer", syncId, r);
            return true;
        }

        private static bool ApplyOneDealer(SQLiteConnection con, JObject r, bool deleted)
        {
            string syncId = r.Value<string>("sync_id");
            if (string.IsNullOrWhiteSpace(syncId)) return true;
            string updated = r.Value<string>("updated_at") ?? DateTime.UtcNow.ToString("o");
            var gate = ClassifyRemoteApply(con, "AddDealer", syncId, updated, deleted, r.Value<string>("device_id"), r.Value<long?>("server_rev"));
            if (gate == RemoteApplyGate.SkipStage)
            {
                StageRemote(con, "zaib_dealers", r);
                return false;
            }
            if (gate == RemoteApplyGate.SkipDone) return true;
            if (deleted) { ApplyRemoteDelete(con, "AddDealer", syncId); return true; }
            using (var chk = new SQLiteCommand("SELECT Did FROM AddDealer WHERE " + WhereSyncIdEq() + " ORDER BY Did ASC LIMIT 1", con))
            {
                BindNormSync(chk, syncId);
                var exists = chk.ExecuteScalar();
                if (exists == null)
                {
                    using (var i = new SQLiteCommand("INSERT INTO AddDealer(DealerName,DDAmount,DAmount,Date,SyncId,UpdatedAt,SyncDirty) VALUES(@n,0,0,@dt,@s,@u,0)", con))
                    {
                        i.Parameters.AddWithValue("@n", r.Value<string>("dealer_name") ?? "");
                        i.Parameters.AddWithValue("@dt", r.Value<string>("date_text") ?? "");
                        i.Parameters.AddWithValue("@s", syncId);
                        i.Parameters.AddWithValue("@u", updated);
                        RequireApplyRows(i.ExecuteNonQuery(), "insert:AddDealer", syncId);
                    }
                }
                else
                {
                    using (var u = new SQLiteCommand("UPDATE AddDealer SET DealerName=@n, Date=@dt, UpdatedAt=@u, SyncDirty=0 WHERE " + WhereSyncIdEq(), con))
                    {
                        u.Parameters.AddWithValue("@n", r.Value<string>("dealer_name") ?? "");
                        u.Parameters.AddWithValue("@dt", r.Value<string>("date_text") ?? "");
                        u.Parameters.AddWithValue("@u", updated);
                        BindNormSync(u, syncId);
                        RequireApplyRows(u.ExecuteNonQuery(), "update:AddDealer", syncId);
                    }
                }
            }
            PersistObservedServerRev(con, "AddDealer", syncId, r);
            return true;
        }

        private static bool ApplyOnePetrol(SQLiteConnection con, JObject r, bool deleted)
        {
            string syncId = r.Value<string>("sync_id");
            if (string.IsNullOrWhiteSpace(syncId)) return true;
            string updated = r.Value<string>("updated_at") ?? DateTime.UtcNow.ToString("o");
            var gate = ClassifyRemoteApply(con, "PetrolAdd", syncId, updated, deleted, r.Value<string>("device_id"), r.Value<long?>("server_rev"));
            if (gate == RemoteApplyGate.SkipStage)
            {
                StageRemote(con, "zaib_petrol_entries", r);
                return false;
            }
            if (gate == RemoteApplyGate.SkipDone) return true;
            if (deleted) { ApplyRemoteDelete(con, "PetrolAdd", syncId); return true; }
            string custSync = r.Value<string>("customer_sync_id");
            object custId = LocalIdBySync(con, "AddCustomer", "id", custSync);
            if (!string.IsNullOrWhiteSpace(custSync) && (custId == null || custId == DBNull.Value))
            {
                StageRemote(con, "zaib_petrol_entries", r);
                return false;
            }
            using (var chk = new SQLiteCommand("SELECT pid FROM PetrolAdd WHERE SyncId=@s", con))
            {
                chk.Parameters.AddWithValue("@s", syncId);
                var exists = chk.ExecuteScalar();
                string sql = exists == null
                    ? "INSERT INTO PetrolAdd(Date,ReceiptNo,vehicle,Litter,Rate,Advance,Amount,Credit,Balance,Note,CustomerId,Processed,IsInitialEntry,SyncId,UpdatedAt,SyncDirty) VALUES(@Date,@ReceiptNo,@vehicle,@Litter,@Rate,@Advance,@Amount,@Credit,@Balance,@Note,@CustomerId,@Processed,@IsInitialEntry,@s,@u,0)"
                    : "UPDATE PetrolAdd SET Date=@Date,ReceiptNo=@ReceiptNo,vehicle=@vehicle,Litter=@Litter,Rate=@Rate,Advance=@Advance,Amount=@Amount,Credit=@Credit,Balance=@Balance,Note=@Note,CustomerId=@CustomerId,Processed=@Processed,IsInitialEntry=@IsInitialEntry,UpdatedAt=@u,SyncDirty=0 WHERE SyncId=@s";
                using (var cmd = new SQLiteCommand(sql, con))
                {
                    cmd.Parameters.AddWithValue("@Date", r.Value<string>("date_text") ?? "");
                    cmd.Parameters.AddWithValue("@ReceiptNo", r.Value<string>("receipt_no") ?? "");
                    cmd.Parameters.AddWithValue("@vehicle", r.Value<string>("vehicle") ?? "");
                    cmd.Parameters.AddWithValue("@Litter", r.Value<double?>("litter") ?? 0);
                    cmd.Parameters.AddWithValue("@Rate", r.Value<double?>("rate") ?? 0);
                    cmd.Parameters.AddWithValue("@Advance", r.Value<double?>("advance") ?? 0);
                    cmd.Parameters.AddWithValue("@Amount", r.Value<double?>("amount") ?? 0);
                    cmd.Parameters.AddWithValue("@Credit", r.Value<double?>("credit") ?? 0);
                    cmd.Parameters.AddWithValue("@Balance", r.Value<double?>("balance") ?? 0);
                    cmd.Parameters.AddWithValue("@Note", r.Value<string>("note") ?? "");
                    cmd.Parameters.AddWithValue("@CustomerId", custId ?? (object)DBNull.Value);
                    cmd.Parameters.AddWithValue("@Processed", r.Value<int?>("processed") ?? 0);
                    cmd.Parameters.AddWithValue("@IsInitialEntry", r.Value<int?>("is_initial_entry") ?? 1);
                    cmd.Parameters.AddWithValue("@s", syncId);
                    cmd.Parameters.AddWithValue("@u", updated);
                    RequireApplyRows(cmd.ExecuteNonQuery(), exists == null ? "insert:PetrolAdd" : "update:PetrolAdd", syncId);
                }
            }
            PersistObservedServerRev(con, "PetrolAdd", syncId, r);
            return true;
        }

        private static bool ApplyOnePurchase(SQLiteConnection con, JObject r, bool deleted)
        {
            string syncId = r.Value<string>("sync_id");
            if (string.IsNullOrWhiteSpace(syncId)) return true;
            string updated = r.Value<string>("updated_at") ?? DateTime.UtcNow.ToString("o");
            var gate = ClassifyRemoteApply(con, "AddStock", syncId, updated, deleted, r.Value<string>("device_id"), r.Value<long?>("server_rev"));
            if (gate == RemoteApplyGate.SkipStage)
            {
                StageRemote(con, "zaib_dealer_purchases", r);
                return false;
            }
            if (gate == RemoteApplyGate.SkipDone) return true;
            if (deleted)
            {
                ReverseDealerBalance(con, syncId);
                ApplyRemoteDelete(con, "AddStock", syncId);
                return true;
            }
            string dSync = r.Value<string>("dealer_sync_id");
            object did = LocalIdBySync(con, "AddDealer", "Did", dSync);
            if (!string.IsNullOrWhiteSpace(dSync) && (did == null || did == DBNull.Value))
            {
                StageRemote(con, "zaib_dealer_purchases", r);
                return false;
            }
            using (var chk = new SQLiteCommand("SELECT Sid FROM AddStock WHERE " + WhereSyncIdEq() + " LIMIT 1", con))
            {
                BindNormSync(chk, syncId);
                var exists = chk.ExecuteScalar();
                string sql = exists == null
                    ? "INSERT INTO AddStock(Vehicle,Rate,SellDisel,Stock,Date,AddDisel,DealerId,Note,SyncId,UpdatedAt,SyncDirty) VALUES(@Vehicle,@Rate,0,0,@Date,@AddDisel,@DealerId,@Note,@s,@u,0)"
                    : "UPDATE AddStock SET Vehicle=@Vehicle,Rate=@Rate,Date=@Date,AddDisel=@AddDisel,DealerId=@DealerId,Note=@Note,UpdatedAt=@u,SyncDirty=0 WHERE " + WhereSyncIdEq();
                using (var cmd = new SQLiteCommand(sql, con))
                {
                    cmd.Parameters.AddWithValue("@Vehicle", r.Value<string>("vehicle") ?? "");
                    cmd.Parameters.AddWithValue("@Rate", r.Value<double?>("rate") ?? 0);
                    cmd.Parameters.AddWithValue("@Date", r.Value<string>("date_text") ?? "");
                    cmd.Parameters.AddWithValue("@AddDisel", r.Value<double?>("add_diesel") ?? 0);
                    cmd.Parameters.AddWithValue("@DealerId", did ?? (object)DBNull.Value);
                    cmd.Parameters.AddWithValue("@Note", r.Value<string>("note") ?? "");
                    cmd.Parameters.AddWithValue("@s", syncId);
                    cmd.Parameters.AddWithValue("@u", updated);
                    if (exists != null)
                        BindNormSync(cmd, syncId);
                    RequireApplyRows(cmd.ExecuteNonQuery(), exists == null ? "insert:AddStock" : "update:AddStock", syncId);
                }
            }
            double addDiesel = r.Value<double?>("add_diesel") ?? 0;
            double rate = r.Value<double?>("rate") ?? 0;
            ReconcileDealerBalance(con, syncId, did, addDiesel * rate, 0);
            PersistObservedServerRev(con, "AddStock", syncId, r);
            return true;
        }

        private static bool ApplyOneStock(SQLiteConnection con, JObject r, bool deleted)
        {
            string syncId = r.Value<string>("sync_id");
            if (string.IsNullOrWhiteSpace(syncId)) return true;
            string updated = r.Value<string>("updated_at") ?? DateTime.UtcNow.ToString("o");
            var gate = ClassifyRemoteApply(con, "StockDiesel", syncId, updated, deleted, r.Value<string>("device_id"), r.Value<long?>("server_rev"));
            if (gate == RemoteApplyGate.SkipStage)
            {
                StageRemote(con, "zaib_stock_diesel", r);
                return false;
            }
            if (gate == RemoteApplyGate.SkipDone) return true;
            if (deleted) { ApplyRemoteDelete(con, "StockDiesel", syncId); return true; }
            string dSync = r.Value<string>("dealer_sync_id");
            object did = LocalIdBySync(con, "AddDealer", "Did", dSync);
            if (!string.IsNullOrWhiteSpace(dSync) && (did == null || did == DBNull.Value))
            {
                StageRemote(con, "zaib_stock_diesel", r);
                return false;
            }
            using (var chk = new SQLiteCommand("SELECT SID FROM StockDiesel WHERE SyncId=@s", con))
            {
                chk.Parameters.AddWithValue("@s", syncId);
                var exists = chk.ExecuteScalar();
                string sql = exists == null
                    ? "INSERT INTO StockDiesel(SDid,Date,Vehicle,Litter,Rate,Credit,Debit,Note,SyncId,UpdatedAt,SyncDirty) VALUES(@SDid,@Date,@Vehicle,@Litter,@Rate,@Credit,@Debit,@Note,@s,@u,0)"
                    : "UPDATE StockDiesel SET SDid=@SDid,Date=@Date,Vehicle=@Vehicle,Litter=@Litter,Rate=@Rate,Credit=@Credit,Debit=@Debit,Note=@Note,UpdatedAt=@u,SyncDirty=0 WHERE SyncId=@s";
                using (var cmd = new SQLiteCommand(sql, con))
                {
                    cmd.Parameters.AddWithValue("@SDid", did ?? (object)DBNull.Value);
                    cmd.Parameters.AddWithValue("@Date", r.Value<string>("date_text") ?? "");
                    cmd.Parameters.AddWithValue("@Vehicle", r.Value<string>("vehicle") ?? "");
                    cmd.Parameters.AddWithValue("@Litter", r.Value<double?>("litter") ?? 0);
                    cmd.Parameters.AddWithValue("@Rate", r.Value<double?>("rate") ?? 0);
                    cmd.Parameters.AddWithValue("@Credit", r.Value<double?>("credit") ?? 0);
                    cmd.Parameters.AddWithValue("@Debit", r.Value<double?>("debit") ?? 0);
                    cmd.Parameters.AddWithValue("@Note", r.Value<string>("note") ?? "");
                    cmd.Parameters.AddWithValue("@s", syncId);
                    cmd.Parameters.AddWithValue("@u", updated);
                    RequireApplyRows(cmd.ExecuteNonQuery(), exists == null ? "insert:StockDiesel" : "update:StockDiesel", syncId);
                }
            }
            PersistObservedServerRev(con, "StockDiesel", syncId, r);
            return true;
        }

        private static bool ApplyOneBank(SQLiteConnection con, JObject r, bool deleted)
        {
            string syncId = r.Value<string>("sync_id");
            if (string.IsNullOrWhiteSpace(syncId)) return true;
            string updated = r.Value<string>("updated_at") ?? DateTime.UtcNow.ToString("o");
            var gate = ClassifyRemoteApply(con, "BankTransactions", syncId, updated, deleted, r.Value<string>("device_id"), r.Value<long?>("server_rev"));
            if (gate == RemoteApplyGate.SkipStage)
            {
                StageRemote(con, "zaib_bank_transactions", r);
                return false;
            }
            if (gate == RemoteApplyGate.SkipDone) return true;
            if (deleted) { ApplyRemoteDelete(con, "BankTransactions", syncId); return true; }
            string cSync = r.Value<string>("customer_sync_id");
            string dSync = r.Value<string>("dealer_sync_id");
            object cid = LocalIdBySync(con, "AddCustomer", "id", cSync);
            object did = LocalIdBySync(con, "AddDealer", "Did", dSync);
            if ((!string.IsNullOrWhiteSpace(cSync) && (cid == null || cid == DBNull.Value)) ||
                (!string.IsNullOrWhiteSpace(dSync) && (did == null || did == DBNull.Value)))
            {
                StageRemote(con, "zaib_bank_transactions", r);
                return false;
            }
            using (var chk = new SQLiteCommand("SELECT Id FROM BankTransactions WHERE SyncId=@s", con))
            {
                chk.Parameters.AddWithValue("@s", syncId);
                var exists = chk.ExecuteScalar();
                string sql = exists == null
                    ? "INSERT INTO BankTransactions(TransactionDate,TransactionType,CustomerId,DealerId,Amount,Note,BankName,SyncId,UpdatedAt,SyncDirty) VALUES(@TransactionDate,@TransactionType,@CustomerId,@DealerId,@Amount,@Note,@BankName,@s,@u,0)"
                    : "UPDATE BankTransactions SET TransactionDate=@TransactionDate,TransactionType=@TransactionType,CustomerId=@CustomerId,DealerId=@DealerId,Amount=@Amount,Note=@Note,BankName=@BankName,UpdatedAt=@u,SyncDirty=0 WHERE SyncId=@s";
                using (var cmd = new SQLiteCommand(sql, con))
                {
                    cmd.Parameters.AddWithValue("@TransactionDate", r.Value<string>("transaction_date") ?? "");
                    cmd.Parameters.AddWithValue("@TransactionType", r.Value<string>("transaction_type") ?? "");
                    cmd.Parameters.AddWithValue("@CustomerId", cid ?? (object)DBNull.Value);
                    cmd.Parameters.AddWithValue("@DealerId", did ?? (object)DBNull.Value);
                    cmd.Parameters.AddWithValue("@Amount", r.Value<double?>("amount") ?? 0);
                    cmd.Parameters.AddWithValue("@Note", r.Value<string>("note") ?? "");
                    cmd.Parameters.AddWithValue("@BankName", r.Value<string>("bank_name") ?? "");
                    cmd.Parameters.AddWithValue("@s", syncId);
                    cmd.Parameters.AddWithValue("@u", updated);
                    RequireApplyRows(cmd.ExecuteNonQuery(), exists == null ? "insert:BankTransactions" : "update:BankTransactions", syncId);
                }
            }
            PersistObservedServerRev(con, "BankTransactions", syncId, r);
            return true;
        }

        private static bool ApplyOneExpense(SQLiteConnection con, JObject r, bool deleted)
        {
            string syncId = r.Value<string>("sync_id");
            if (string.IsNullOrWhiteSpace(syncId)) return true;
            string updated = r.Value<string>("updated_at") ?? DateTime.UtcNow.ToString("o");
            var gate = ClassifyRemoteApply(con, "Expensetable", syncId, updated, deleted, r.Value<string>("device_id"), r.Value<long?>("server_rev"));
            if (gate == RemoteApplyGate.SkipStage)
            {
                StageRemote(con, "zaib_expenses", r);
                return false;
            }
            if (gate == RemoteApplyGate.SkipDone) return true;
            if (deleted) { ApplyRemoteDelete(con, "Expensetable", syncId); return true; }
            using (var chk = new SQLiteCommand("SELECT sid FROM Expensetable WHERE SyncId=@s", con))
            {
                chk.Parameters.AddWithValue("@s", syncId);
                var exists = chk.ExecuteScalar();
                string sql = exists == null
                    ? "INSERT INTO Expensetable(Name,Category,Amount,EDate,Note,SyncId,UpdatedAt,SyncDirty) VALUES(@Name,@Category,@Amount,@EDate,@Note,@s,@u,0)"
                    : "UPDATE Expensetable SET Name=@Name,Category=@Category,Amount=@Amount,EDate=@EDate,Note=@Note,UpdatedAt=@u,SyncDirty=0 WHERE SyncId=@s";
                using (var cmd = new SQLiteCommand(sql, con))
                {
                    cmd.Parameters.AddWithValue("@Name", r.Value<string>("name") ?? "");
                    cmd.Parameters.AddWithValue("@Category", r.Value<string>("category") ?? "");
                    cmd.Parameters.AddWithValue("@Amount", r.Value<double?>("amount") ?? 0);
                    cmd.Parameters.AddWithValue("@EDate", r.Value<string>("e_date") ?? "");
                    cmd.Parameters.AddWithValue("@Note", r.Value<string>("note") ?? "");
                    cmd.Parameters.AddWithValue("@s", syncId);
                    cmd.Parameters.AddWithValue("@u", updated);
                    RequireApplyRows(cmd.ExecuteNonQuery(), exists == null ? "insert:Expensetable" : "update:Expensetable", syncId);
                }
            }
            PersistObservedServerRev(con, "Expensetable", syncId, r);
            return true;
        }

        private static bool ApplyOneDealerTransfer(SQLiteConnection con, JObject r, bool deleted)
        {
            if (!TableExistsLocal(con, "DealertoDealer")) return true;
            string syncId = r.Value<string>("sync_id");
            if (string.IsNullOrWhiteSpace(syncId)) return true;
            string updated = r.Value<string>("updated_at") ?? DateTime.UtcNow.ToString("o");
            var gate = ClassifyRemoteApply(con, "DealertoDealer", syncId, updated, deleted, r.Value<string>("device_id"), r.Value<long?>("server_rev"));
            if (gate == RemoteApplyGate.SkipStage)
            {
                StageRemote(con, "zaib_dealer_transfers", r);
                return false;
            }
            if (gate == RemoteApplyGate.SkipDone) return true;
            if (deleted)
            {
                ReverseDealerBalance(con, syncId + ":from");
                ReverseDealerBalance(con, syncId + ":to");
                ApplyRemoteDelete(con, "DealertoDealer", syncId);
                return true;
            }
            string fromSync = r.Value<string>("from_dealer_sync_id") ?? "";
            string toSync = r.Value<string>("to_dealer_sync_id") ?? "";
            object fromId = LocalIdBySync(con, "AddDealer", "Did", fromSync);
            object toId = LocalIdBySync(con, "AddDealer", "Did", toSync);
            if ((!string.IsNullOrWhiteSpace(fromSync) && (fromId == null || fromId == DBNull.Value)) ||
                (!string.IsNullOrWhiteSpace(toSync) && (toId == null || toId == DBNull.Value)))
            {
                StageRemote(con, "zaib_dealer_transfers", r);
                return false;
            }
            double amount = r.Value<double?>("amount") ?? r.Value<double?>("amount_given") ?? 0;
            string dateText = r.Value<string>("date_text") ?? "";
            string note = r.Value<string>("note") ?? "";
            string firstName = "";
            string secondName = "";
            if (fromId != null && fromId != DBNull.Value)
            {
                using (var n = new SQLiteCommand("SELECT DealerName FROM AddDealer WHERE Did=@id LIMIT 1", con))
                {
                    n.Parameters.AddWithValue("@id", fromId);
                    firstName = Convert.ToString(n.ExecuteScalar()) ?? "";
                }
            }
            if (toId != null && toId != DBNull.Value)
            {
                using (var n = new SQLiteCommand("SELECT DealerName FROM AddDealer WHERE Did=@id LIMIT 1", con))
                {
                    n.Parameters.AddWithValue("@id", toId);
                    secondName = Convert.ToString(n.ExecuteScalar()) ?? "";
                }
            }
            using (var chk = new SQLiteCommand("SELECT LedgerID FROM DealertoDealer WHERE SyncId=@s", con))
            {
                chk.Parameters.AddWithValue("@s", syncId);
                var exists = chk.ExecuteScalar();
                if (exists == null)
                {
                    using (var i = new SQLiteCommand(
                        @"INSERT INTO DealertoDealer(Date,FirstDealer,SecondDealer,AmounGiven,Note,id,Did,SyncId,UpdatedAt,SyncDirty)
                          VALUES(@dt,@n1,@n2,@amt,@note,@d1,@d2,@s,@u,0)", con))
                    {
                        i.Parameters.AddWithValue("@dt", dateText);
                        i.Parameters.AddWithValue("@n1", firstName);
                        i.Parameters.AddWithValue("@n2", secondName);
                        i.Parameters.AddWithValue("@amt", amount);
                        i.Parameters.AddWithValue("@note", note);
                        i.Parameters.AddWithValue("@d1", fromId ?? (object)DBNull.Value);
                        i.Parameters.AddWithValue("@d2", toId ?? (object)DBNull.Value);
                        i.Parameters.AddWithValue("@s", syncId);
                        i.Parameters.AddWithValue("@u", updated);
                        RequireApplyRows(i.ExecuteNonQuery(), "insert:DealertoDealer", syncId);
                    }
                }
                else
                {
                    using (var u = new SQLiteCommand(
                        @"UPDATE DealertoDealer SET Date=@dt, FirstDealer=@n1, SecondDealer=@n2, AmounGiven=@amt, Note=@note,
                          id=@d1, Did=@d2, UpdatedAt=@u, SyncDirty=0 WHERE SyncId=@s", con))
                    {
                        u.Parameters.AddWithValue("@dt", dateText);
                        u.Parameters.AddWithValue("@n1", firstName);
                        u.Parameters.AddWithValue("@n2", secondName);
                        u.Parameters.AddWithValue("@amt", amount);
                        u.Parameters.AddWithValue("@note", note);
                        u.Parameters.AddWithValue("@d1", fromId ?? (object)DBNull.Value);
                        u.Parameters.AddWithValue("@d2", toId ?? (object)DBNull.Value);
                        u.Parameters.AddWithValue("@u", updated);
                        u.Parameters.AddWithValue("@s", syncId);
                        RequireApplyRows(u.ExecuteNonQuery(), "update:DealertoDealer", syncId);
                    }
                }
            }
            ReconcileDealerBalance(con, syncId + ":from", fromId, 0, amount);
            ReconcileDealerBalance(con, syncId + ":to", toId, amount, 0);
            PersistObservedServerRev(con, "DealertoDealer", syncId, r);
            return true;
        }

        private static void MarkCleanIfVersion(SQLiteConnection con, string table, string pk, object id, string syncId, string uploadedUpdatedAt)
        {
            using (var cmd = new SQLiteCommand(
                $"UPDATE {table} SET SyncDirty=0, SyncId=@s WHERE {pk}=@id AND UpdatedAt=@u", con))
            {
                cmd.Parameters.AddWithValue("@s", syncId);
                cmd.Parameters.AddWithValue("@id", id);
                cmd.Parameters.AddWithValue("@u", uploadedUpdatedAt ?? "");
                cmd.ExecuteNonQuery();
            }
        }

        private static bool ParentReady(SQLiteConnection con, string table, string pk, object id)
        {
            if (id == null || id == DBNull.Value) return true;
            using (var cmd = new SQLiteCommand(
                $"SELECT SyncId, SyncDirty FROM {table} WHERE {pk}=@id LIMIT 1", con))
            {
                cmd.Parameters.AddWithValue("@id", id);
                using (var r = cmd.ExecuteReader())
                {
                    if (!r.Read()) return false;
                    string syncId = r["SyncId"] == DBNull.Value ? null : Convert.ToString(r["SyncId"]);
                    if (string.IsNullOrWhiteSpace(syncId)) return false;
                    int dirty = r["SyncDirty"] == DBNull.Value ? 0 : Convert.ToInt32(r["SyncDirty"]);
                    return dirty == 0;
                }
            }
        }

        private static string RowUpdatedAt(DataRow r)
        {
            string v = Convert.ToString(r["UpdatedAt"]);
            return string.IsNullOrWhiteSpace(v) ? DateTime.UtcNow.ToString("o") : v;
        }

        private static void AdvanceCheckpoint(string updatedAt, string syncId, ref string appliedMax)
        {
            if (string.IsNullOrWhiteSpace(updatedAt)) return;
            SplitCheckpoint(appliedMax ?? "", out var curAt, out var curId);
            DateTime u = DateTime.TryParse(updatedAt, out var ut) ? ut.ToUniversalTime() : DateTime.MinValue;
            DateTime m = DateTime.TryParse(curAt, out var mt) ? mt.ToUniversalTime() : DateTime.MinValue;
            int cmp = u.CompareTo(m);
            if (cmp > 0 || (cmp == 0 && string.CompareOrdinal(syncId ?? "", curId ?? "") > 0))
                appliedMax = updatedAt + "|" + (syncId ?? "");
        }

        private static void StageRemote(SQLiteConnection con, string cloudTable, JObject row)
        {
            string syncId = row.Value<string>("sync_id");
            if (string.IsNullOrWhiteSpace(syncId)) return;
            using (var cmd = new SQLiteCommand(
                @"INSERT OR REPLACE INTO SyncStagedRemote(SyncId,CloudTable,PayloadJson,UpdatedAt)
                  VALUES(@s,@t,@p,@u)", con))
            {
                cmd.Parameters.AddWithValue("@s", syncId);
                cmd.Parameters.AddWithValue("@t", cloudTable);
                cmd.Parameters.AddWithValue("@p", row.ToString(Formatting.None));
                cmd.Parameters.AddWithValue("@u", row.Value<string>("updated_at") ?? DateTime.UtcNow.ToString("o"));
                cmd.ExecuteNonQuery();
            }
        }

        private static string GetSyncId(SQLiteConnection con, string table, string pk, object id)
        {
            if (id == null || id == DBNull.Value) return null;
            using (var cmd = new SQLiteCommand($"SELECT SyncId FROM {table} WHERE {pk}=@id LIMIT 1", con))
            {
                cmd.Parameters.AddWithValue("@id", id);
                var v = cmd.ExecuteScalar();
                return v == null || v == DBNull.Value ? null : v.ToString();
            }
        }

        private static DataTable QueryDirty(SQLiteConnection con, string sql)
        {
            using (var da = new SQLiteDataAdapter(sql, con))
            {
                var dt = new DataTable();
                da.Fill(dt);
                return dt;
            }
        }

        private static async Task PushCustomers(SQLiteConnection con)
        {
            var dt = QueryDirty(con, "SELECT * FROM AddCustomer WHERE IFNULL(SyncDirty,1)=1");
            foreach (DataRow r in dt.Rows)
            {
                string syncId = string.IsNullOrWhiteSpace(Convert.ToString(r["SyncId"])) ? Guid.NewGuid().ToString() : Convert.ToString(r["SyncId"]);
                string updatedAt = RowUpdatedAt(r);
                try
                {
                    var ack = await UpsertAsync("zaib_customers", new {
                        sync_id = syncId,
                        local_id = r["id"],
                        name = Convert.ToString(r["Name"]) ?? "",
                        mobile = Convert.ToString(r["Mobile"]) ?? "",
                        date_text = Convert.ToString(r["Date"]) ?? "",
                        updated_at = updatedAt,
                        deleted_at = (string)null,
                        device_id = _deviceId
                    }, ReadServerRev(con, "AddCustomer", "id", r["id"]));
                    await FinishUpload(con, "zaib_customers", "AddCustomer", "id", r["id"], syncId, updatedAt, JsonConvert.SerializeObject(new { sync_id = syncId, updated_at = updatedAt, device_id = _deviceId }), ack);
                }
                catch (Exception ex) { LogFail(con, "push:zaib_customers", syncId, ex); }
            }
        }

        private static async Task PushDealers(SQLiteConnection con)
        {
            var dt = QueryDirty(con, "SELECT * FROM AddDealer WHERE IFNULL(SyncDirty,1)=1");
            foreach (DataRow r in dt.Rows)
            {
                string syncId = string.IsNullOrWhiteSpace(Convert.ToString(r["SyncId"])) ? Guid.NewGuid().ToString() : Convert.ToString(r["SyncId"]);
                string updatedAt = RowUpdatedAt(r);
                try
                {
                    var ack = await UpsertAsync("zaib_dealers", new {
                        sync_id = syncId,
                        local_id = r["Did"],
                        dealer_name = Convert.ToString(r["DealerName"]) ?? "",
                        dd_amount = r["DDAmount"] == DBNull.Value ? 0 : Convert.ToDouble(r["DDAmount"]),
                        d_amount = r["DAmount"] == DBNull.Value ? 0 : Convert.ToDouble(r["DAmount"]),
                        date_text = Convert.ToString(r["Date"]) ?? "",
                        updated_at = updatedAt,
                        deleted_at = (string)null,
                        device_id = _deviceId
                    }, ReadServerRev(con, "AddDealer", "Did", r["Did"]));
                    await FinishUpload(con, "zaib_dealers", "AddDealer", "Did", r["Did"], syncId, updatedAt, JsonConvert.SerializeObject(new { sync_id = syncId, updated_at = updatedAt, device_id = _deviceId }), ack);
                }
                catch (Exception ex) { LogFail(con, "push:zaib_dealers", syncId, ex); }
            }
        }

        private static async Task PushDealerBalanceOps(SQLiteConnection con)
        {
            if (!TableExists(con, "SyncDealerBalanceOp")) return;
            var dt = QueryDirty(con, "SELECT * FROM SyncDealerBalanceOp WHERE IFNULL(SyncDirty,1)=1");
            foreach (DataRow r in dt.Rows)
            {
                string syncId = Convert.ToString(r["SyncId"]);
                if (string.IsNullOrWhiteSpace(syncId)) continue;
                string dealerSync = Convert.ToString(r["DealerSyncId"]) ?? "";
                if (string.IsNullOrWhiteSpace(dealerSync)) continue;
                string updatedAt = RowUpdatedAt(r);
                string deletedAt = r["DeletedAt"] == DBNull.Value ? null : Convert.ToString(r["DeletedAt"]);
                try
                {
                    var ack = await UpsertAsync("zaib_dealer_balance_ops", new {
                        sync_id = syncId,
                        dealer_sync_id = dealerSync,
                        dd_delta = ToD(r["DdDelta"]),
                        d_delta = ToD(r["DDelta"]),
                        source_kind = Convert.ToString(r["SourceKind"]) ?? "manual",
                        source_sync_id = Convert.ToString(r["SourceSyncId"]) ?? syncId,
                        date_text = Convert.ToString(r["DateText"]) ?? "",
                        note = Convert.ToString(r["Note"]) ?? "",
                        updated_at = updatedAt,
                        deleted_at = deletedAt,
                        device_id = _deviceId
                    }, ReadServerRev(con, "SyncDealerBalanceOp", "SyncId", syncId));
                    await FinishUpload(con, "zaib_dealer_balance_ops", "SyncDealerBalanceOp", "SyncId", syncId, syncId, updatedAt,
                        JsonConvert.SerializeObject(new { sync_id = syncId, updated_at = updatedAt, device_id = _deviceId }), ack);
                }
                catch (Exception ex) { LogFail(con, "push:zaib_dealer_balance_ops", syncId, ex); }
            }
        }

        private static async Task PushPetrol(SQLiteConnection con)
        {
            var dt = QueryDirty(con, "SELECT * FROM PetrolAdd WHERE IFNULL(SyncDirty,1)=1");
            foreach (DataRow r in dt.Rows)
            {
                if (!ParentReady(con, "AddCustomer", "id", r["CustomerId"])) continue;
                string syncId = string.IsNullOrWhiteSpace(Convert.ToString(r["SyncId"])) ? Guid.NewGuid().ToString() : Convert.ToString(r["SyncId"]);
                string updatedAt = RowUpdatedAt(r);
                string custSync = GetSyncId(con, "AddCustomer", "id", r["CustomerId"]);
                string customerName = "";
                if (r["CustomerId"] != DBNull.Value)
                {
                    using (var c = new SQLiteCommand("SELECT Name FROM AddCustomer WHERE id=@id", con))
                    {
                        c.Parameters.AddWithValue("@id", r["CustomerId"]);
                        var v = c.ExecuteScalar();
                        customerName = v?.ToString() ?? "";
                    }
                }
                try
                {
                    var ack = await UpsertAsync("zaib_petrol_entries", new {
                        sync_id = syncId,
                        local_id = r["pid"],
                        customer_sync_id = custSync,
                        customer_name = customerName,
                        date_text = Convert.ToString(r["Date"]) ?? "",
                        receipt_no = Convert.ToString(r["ReceiptNo"]) ?? "",
                        vehicle = Convert.ToString(r["vehicle"]) ?? "",
                        litter = ToD(r["Litter"]),
                        rate = ToD(r["Rate"]),
                        advance = ToD(r["Advance"]),
                        amount = ToD(r["Amount"]),
                        credit = ToD(r["Credit"]),
                        balance = ToD(r["Balance"]),
                        note = Convert.ToString(r["Note"]) ?? "",
                        is_initial_entry = r.Table.Columns.Contains("IsInitialEntry") ? (r["IsInitialEntry"] == DBNull.Value ? 1 : Convert.ToInt32(r["IsInitialEntry"])) : 1,
                        processed = r.Table.Columns.Contains("Processed") ? (r["Processed"] == DBNull.Value ? 0 : Convert.ToInt32(r["Processed"])) : 0,
                        updated_at = updatedAt,
                        deleted_at = (string)null,
                        device_id = _deviceId
                    }, ReadServerRev(con, "PetrolAdd", "pid", r["pid"]));
                    await FinishUpload(con, "zaib_petrol_entries", "PetrolAdd", "pid", r["pid"], syncId, updatedAt, JsonConvert.SerializeObject(new { sync_id = syncId, updated_at = updatedAt, device_id = _deviceId }), ack);
                }
                catch (Exception ex) { LogFail(con, "push:zaib_petrol_entries", syncId, ex); }
            }
        }

        private static async Task PushPayouts(SQLiteConnection con)
        {
            var dt = QueryDirty(con, "SELECT * FROM DieselLedgerCredit WHERE IFNULL(SyncDirty,1)=1");
            foreach (DataRow r in dt.Rows)
            {
                if (!ParentReady(con, "AddDealer", "Did", r["Did"])) continue;
                string syncId = string.IsNullOrWhiteSpace(Convert.ToString(r["SyncId"])) ? Guid.NewGuid().ToString() : Convert.ToString(r["SyncId"]);
                string updatedAt = RowUpdatedAt(r);
                string dSync = GetSyncId(con, "AddDealer", "Did", r["Did"]);
                try
                {
                    var ack = await UpsertAsync("zaib_dealer_payouts", new {
                        sync_id = syncId,
                        local_id = r["LedgerID"],
                        dealer_sync_id = dSync,
                        dealer_name = "",
                        amount_given = ToD(r["AmounGiven"]),
                        date_text = Convert.ToString(r["Date"]) ?? "",
                        note = Convert.ToString(r["Note"]) ?? "",
                        updated_at = updatedAt,
                        deleted_at = (string)null,
                        device_id = _deviceId
                    }, ReadServerRev(con, "DieselLedgerCredit", "LedgerID", r["LedgerID"]));
                    await FinishUpload(con, "zaib_dealer_payouts", "DieselLedgerCredit", "LedgerID", r["LedgerID"], syncId, updatedAt, JsonConvert.SerializeObject(new { sync_id = syncId, updated_at = updatedAt, device_id = _deviceId }), ack);
                }
                catch (Exception ex) { LogFail(con, "push:zaib_dealer_payouts", syncId, ex); }
            }
        }

        private static async Task PushPurchases(SQLiteConnection con)
        {
            var dt = QueryDirty(con, "SELECT * FROM AddStock WHERE IFNULL(SyncDirty,1)=1");
            foreach (DataRow r in dt.Rows)
            {
                if (!ParentReady(con, "AddDealer", "Did", r["DealerId"])) continue;
                string syncId = string.IsNullOrWhiteSpace(Convert.ToString(r["SyncId"])) ? Guid.NewGuid().ToString() : Convert.ToString(r["SyncId"]);
                string updatedAt = RowUpdatedAt(r);
                string dSync = GetSyncId(con, "AddDealer", "Did", r["DealerId"]);
                try
                {
                    var ack = await UpsertAsync("zaib_dealer_purchases", new {
                        sync_id = syncId,
                        local_id = r["Sid"],
                        dealer_sync_id = dSync,
                        dealer_name = "",
                        vehicle = Convert.ToString(r["Vehicle"]) ?? "",
                        rate = ToD(r["Rate"]),
                        add_diesel = ToD(r["AddDisel"]),
                        date_text = Convert.ToString(r["Date"]) ?? "",
                        note = Convert.ToString(r["Note"]) ?? "",
                        updated_at = updatedAt,
                        deleted_at = (string)null,
                        device_id = _deviceId
                    }, ReadServerRev(con, "AddStock", "Sid", r["Sid"]));
                    await FinishUpload(con, "zaib_dealer_purchases", "AddStock", "Sid", r["Sid"], syncId, updatedAt, JsonConvert.SerializeObject(new { sync_id = syncId, updated_at = updatedAt, device_id = _deviceId }), ack);
                }
                catch (Exception ex) { LogFail(con, "push:zaib_dealer_purchases", syncId, ex); }
            }
        }

        private static async Task PushDirect(SQLiteConnection con)
        {
            var dt = QueryDirty(con, "SELECT * FROM DieselLedgerDebit WHERE IFNULL(SyncDirty,1)=1");
            foreach (DataRow r in dt.Rows)
            {
                if (!ParentReady(con, "AddDealer", "Did", r["Did"])) continue;
                string syncId = string.IsNullOrWhiteSpace(Convert.ToString(r["SyncId"])) ? Guid.NewGuid().ToString() : Convert.ToString(r["SyncId"]);
                string updatedAt = RowUpdatedAt(r);
                string dSync = GetSyncId(con, "AddDealer", "Did", r["Did"]);
                try
                {
                    var ack = await UpsertAsync("zaib_dealer_direct", new {
                        sync_id = syncId,
                        local_id = r["LedgerID"],
                        dealer_sync_id = dSync,
                        dealer_name = "",
                        amount_given = ToD(r["AmounGiven"]),
                        date_text = Convert.ToString(r["Date"]) ?? "",
                        note = Convert.ToString(r["Note"]) ?? "",
                        updated_at = updatedAt,
                        deleted_at = (string)null,
                        device_id = _deviceId
                    }, ReadServerRev(con, "DieselLedgerDebit", "LedgerID", r["LedgerID"]));
                    await FinishUpload(con, "zaib_dealer_direct", "DieselLedgerDebit", "LedgerID", r["LedgerID"], syncId, updatedAt, JsonConvert.SerializeObject(new { sync_id = syncId, updated_at = updatedAt, device_id = _deviceId }), ack);
                }
                catch (Exception ex) { LogFail(con, "push:zaib_dealer_direct", syncId, ex); }
            }
        }

        private static async Task PushStock(SQLiteConnection con)
        {
            var dt = QueryDirty(con, "SELECT * FROM StockDiesel WHERE IFNULL(SyncDirty,1)=1");
            foreach (DataRow r in dt.Rows)
            {
                // Roznamcha Add2: SDid = StockPartyName.Pid (AddDealer nahi).
                // Inhe cloud dealer-parent se mat jodo — warna galat dealer pe push / local wipe.
                bool isStockParty = false;
                try
                {
                    using (var chk = new SQLiteCommand(
                        "SELECT 1 FROM StockPartyName WHERE Pid=@id LIMIT 1", con))
                    {
                        chk.Parameters.AddWithValue("@id", r["SDid"] ?? DBNull.Value);
                        isStockParty = chk.ExecuteScalar() != null;
                    }
                }
                catch { isStockParty = false; }
                if (isStockParty)
                    continue;

                if (!ParentReady(con, "AddDealer", "Did", r["SDid"])) continue;
                string syncId = string.IsNullOrWhiteSpace(Convert.ToString(r["SyncId"])) ? Guid.NewGuid().ToString() : Convert.ToString(r["SyncId"]);
                string updatedAt = RowUpdatedAt(r);
                string dSync = GetSyncId(con, "AddDealer", "Did", r["SDid"]);
                try
                {
                    var ack = await UpsertAsync("zaib_stock_diesel", new {
                        sync_id = syncId,
                        local_id = r["SID"],
                        dealer_sync_id = dSync,
                        dealer_name = "",
                        date_text = Convert.ToString(r["Date"]) ?? "",
                        vehicle = Convert.ToString(r["Vehicle"]) ?? "",
                        litter = ToD(r["Litter"]),
                        rate = ToD(r["Rate"]),
                        credit = ToD(r["Credit"]),
                        debit = ToD(r["Debit"]),
                        note = Convert.ToString(r["Note"]) ?? "",
                        updated_at = updatedAt,
                        deleted_at = (string)null,
                        device_id = _deviceId
                    }, ReadServerRev(con, "StockDiesel", "SID", r["SID"]));
                    await FinishUpload(con, "zaib_stock_diesel", "StockDiesel", "SID", r["SID"], syncId, updatedAt, JsonConvert.SerializeObject(new { sync_id = syncId, updated_at = updatedAt, device_id = _deviceId }), ack);
                }
                catch (Exception ex) { LogFail(con, "push:zaib_stock_diesel", syncId, ex); }
            }
        }

        private static async Task PushBank(SQLiteConnection con)
        {
            var dt = QueryDirty(con, "SELECT * FROM BankTransactions WHERE IFNULL(SyncDirty,1)=1");
            foreach (DataRow r in dt.Rows)
            {
                if (r["CustomerId"] != DBNull.Value && !ParentReady(con, "AddCustomer", "id", r["CustomerId"])) continue;
                if (r["DealerId"] != DBNull.Value && !ParentReady(con, "AddDealer", "Did", r["DealerId"])) continue;
                string syncId = string.IsNullOrWhiteSpace(Convert.ToString(r["SyncId"])) ? Guid.NewGuid().ToString() : Convert.ToString(r["SyncId"]);
                string updatedAt = RowUpdatedAt(r);
                string cSync = GetSyncId(con, "AddCustomer", "id", r["CustomerId"]);
                string dSync = GetSyncId(con, "AddDealer", "Did", r["DealerId"]);
                try
                {
                    var ack = await UpsertAsync("zaib_bank_transactions", new {
                        sync_id = syncId,
                        local_id = r["Id"],
                        transaction_date = Convert.ToString(r["TransactionDate"]) ?? "",
                        transaction_type = Convert.ToString(r["TransactionType"]) ?? "",
                        customer_sync_id = cSync,
                        customer_name = "",
                        dealer_sync_id = dSync,
                        dealer_name = "",
                        amount = ToD(r["Amount"]),
                        note = Convert.ToString(r["Note"]) ?? "",
                        bank_name = Convert.ToString(r["BankName"]) ?? "",
                        updated_at = updatedAt,
                        deleted_at = (string)null,
                        device_id = _deviceId
                    }, ReadServerRev(con, "BankTransactions", "Id", r["Id"]));
                    await FinishUpload(con, "zaib_bank_transactions", "BankTransactions", "Id", r["Id"], syncId, updatedAt, JsonConvert.SerializeObject(new { sync_id = syncId, updated_at = updatedAt, device_id = _deviceId }), ack);
                }
                catch (Exception ex) { LogFail(con, "push:zaib_bank_transactions", syncId, ex); }
            }
        }

        private static async Task PushExpenses(SQLiteConnection con)
        {
            if (!TableExists(con, "Expensetable")) return;
            var dt = QueryDirty(con, "SELECT * FROM Expensetable WHERE IFNULL(SyncDirty,1)=1");
            foreach (DataRow r in dt.Rows)
            {
                string syncId = string.IsNullOrWhiteSpace(Convert.ToString(r["SyncId"])) ? Guid.NewGuid().ToString() : Convert.ToString(r["SyncId"]);
                string updatedAt = RowUpdatedAt(r);
                try
                {
                    var ack = await UpsertAsync("zaib_expenses", new {
                        sync_id = syncId,
                        local_id = r["sid"],
                        name = Convert.ToString(r["Name"]) ?? "",
                        category = Convert.ToString(r["Category"]) ?? "",
                        amount = ToD(r["Amount"]),
                        e_date = Convert.ToString(r["EDate"]) ?? "",
                        note = Convert.ToString(r["Note"]) ?? "",
                        updated_at = updatedAt,
                        deleted_at = (string)null,
                        device_id = _deviceId
                    }, ReadServerRev(con, "Expensetable", "sid", r["sid"]));
                    await FinishUpload(con, "zaib_expenses", "Expensetable", "sid", r["sid"], syncId, updatedAt, JsonConvert.SerializeObject(new { sync_id = syncId, updated_at = updatedAt, device_id = _deviceId }), ack);
                }
                catch (Exception ex) { LogFail(con, "push:zaib_expenses", syncId, ex); }
            }
        }

        private static async Task PushDealerTransfers(SQLiteConnection con)
        {
            if (!TableExistsLocal(con, "DealertoDealer")) return;
            var dt = QueryDirty(con, "SELECT * FROM DealertoDealer WHERE IFNULL(SyncDirty,1)=1");
            foreach (DataRow r in dt.Rows)
            {
                string syncId = string.IsNullOrWhiteSpace(Convert.ToString(r["SyncId"])) ? Guid.NewGuid().ToString() : Convert.ToString(r["SyncId"]);
                string updatedAt = RowUpdatedAt(r);
                int fromLocal = r["id"] == DBNull.Value ? 0 : Convert.ToInt32(r["id"]);
                int toLocal = r["Did"] == DBNull.Value ? 0 : Convert.ToInt32(r["Did"]);
                string fromSync = fromLocal > 0 ? GetSyncId(con, "AddDealer", "Did", fromLocal) : null;
                string toSync = toLocal > 0 ? GetSyncId(con, "AddDealer", "Did", toLocal) : null;
                if (string.IsNullOrWhiteSpace(fromSync) || string.IsNullOrWhiteSpace(toSync)) continue;
                try
                {
                    var ack = await UpsertAsync("zaib_dealer_transfers", new {
                        sync_id = syncId,
                        local_id = r["LedgerID"],
                        from_dealer_sync_id = fromSync,
                        to_dealer_sync_id = toSync,
                        amount = ToD(r["AmounGiven"]),
                        date_text = Convert.ToString(r["Date"]) ?? "",
                        note = Convert.ToString(r["Note"]) ?? "",
                        updated_at = updatedAt,
                        deleted_at = (string)null,
                        device_id = _deviceId
                    }, ReadServerRev(con, "DealertoDealer", "LedgerID", r["LedgerID"]));
                    await FinishUpload(con, "zaib_dealer_transfers", "DealertoDealer", "LedgerID", r["LedgerID"], syncId, updatedAt,
                        JsonConvert.SerializeObject(new { sync_id = syncId, updated_at = updatedAt, device_id = _deviceId }), ack);
                }
                catch (Exception ex) { LogFail(con, "push:zaib_dealer_transfers", syncId, ex); }
            }
        }

        private static async Task PushTombstones(SQLiteConnection con)
        {
            var dt = QueryDirty(con, "SELECT * FROM SyncTombstone");
            foreach (DataRow r in dt.Rows)
            {
                string table = Convert.ToString(r["CloudTable"]);
                string syncId = Convert.ToString(r["SyncId"]);
                string deletedAt = Convert.ToString(r["DeletedAt"]);
                long? expectedRev = null;
                if (r.Table.Columns.Contains("ExpectedServerRev") && r["ExpectedServerRev"] != DBNull.Value)
                {
                    try
                    {
                        long v = Convert.ToInt64(r["ExpectedServerRev"]);
                        if (v > 0) expectedRev = v;
                    }
                    catch { }
                }
                string requestId = null;
                if (r.Table.Columns.Contains("RequestId") && r["RequestId"] != DBNull.Value)
                    requestId = Convert.ToString(r["RequestId"]);
                if (string.IsNullOrWhiteSpace(requestId))
                    requestId = LocalPersistence.StableTombstoneRequestId(table, syncId, deletedAt);
                try
                {
                    var ack = await UpsertAsync(table, new
                    {
                        sync_id = syncId,
                        updated_at = deletedAt,
                        deleted_at = deletedAt,
                        device_id = _deviceId
                    }, expectedRev, requestId);
                    if (ack == UploadAck.Accepted || ack == UploadAck.Duplicate || ack == UploadAck.Deleted)
                    {
                        using (var d = new SQLiteCommand(
                            "DELETE FROM SyncTombstone WHERE SyncId=@s AND DeletedAt=@d", con))
                        {
                            d.Parameters.AddWithValue("@s", syncId);
                            d.Parameters.AddWithValue("@d", deletedAt ?? "");
                            d.ExecuteNonQuery();
                        }
                    }
                    else if (ack == UploadAck.Conflict)
                    {
                        // Evidence-only on conflict. Legacy (null expected_rev): stamp live rev for retry.
                        // Do not blind-overwrite cloud.
                        try
                        {
                            var res = await Http.GetAsync($"{Url}/rest/v1/{table}?sync_id=eq.{Uri.EscapeDataString(syncId)}&select=deleted_at,server_rev&limit=1");
                            if (res.IsSuccessStatusCode)
                            {
                                var body = await res.Content.ReadAsStringAsync();
                                var arr = JArray.Parse(string.IsNullOrWhiteSpace(body) ? "[]" : body);
                                if (arr.Count > 0 && arr[0]["deleted_at"] != null && arr[0]["deleted_at"].Type != JTokenType.Null)
                                {
                                    using (var d = new SQLiteCommand(
                                        "DELETE FROM SyncTombstone WHERE SyncId=@s AND DeletedAt=@d", con))
                                    {
                                        d.Parameters.AddWithValue("@s", syncId);
                                        d.Parameters.AddWithValue("@d", deletedAt ?? "");
                                        d.ExecuteNonQuery();
                                    }
                                }
                                else
                                {
                                    // Legacy tombstone (AD trigger wiped OCC): learn live server_rev once
                                    // so the next pass can soft-delete with a real expected_rev.
                                    // Do NOT StageRemote the live row — that would resurrect the local delete.
                                    if (expectedRev == null && arr.Count > 0 && arr[0]["server_rev"] != null
                                        && arr[0]["server_rev"].Type != JTokenType.Null)
                                    {
                                        try
                                        {
                                            long liveRev = arr[0].Value<long>("server_rev");
                                            if (liveRev > 0)
                                            {
                                                string req = LocalPersistence.StableTombstoneRequestId(
                                                    table, syncId, deletedAt);
                                                using (var u = new SQLiteCommand(
                                                    @"UPDATE SyncTombstone
                                                      SET ExpectedServerRev=@r, RequestId=@req
                                                      WHERE SyncId=@s AND DeletedAt=@d", con))
                                                {
                                                    u.Parameters.AddWithValue("@r", liveRev);
                                                    u.Parameters.AddWithValue("@req", req);
                                                    u.Parameters.AddWithValue("@s", syncId);
                                                    u.Parameters.AddWithValue("@d", deletedAt ?? "");
                                                    u.ExecuteNonQuery();
                                                }
                                                LogFail(con, "push:tombstone:rev_repaired", syncId,
                                                    new Exception("stamped ExpectedServerRev=" + liveRev + " for retry"));
                                            }
                                        }
                                        catch (Exception repairEx)
                                        {
                                            LogFail(con, "push:tombstone:rev_repair_fail", syncId, repairEx);
                                        }
                                    }
                                    else
                                    {
                                        LogFail(con, "push:tombstone:conflict", syncId,
                                            new Exception(expectedRev == null
                                                ? "legacy tombstone missing ExpectedServerRev — retain delete intent"
                                                : "delete conflict — retain tombstone; no blind overwrite"));
                                        // Real OCC conflict on delete: stage server evidence for review only.
                                        if (arr.Count > 0 && arr[0] is JObject jo)
                                            StageRemote(con, table, jo);
                                    }
                                }
                            }
                        }
                        catch { }
                    }
                }
                catch (Exception ex) { LogFail(con, "push:tombstone", syncId, ex); }
            }
        }

        private static double ToD(object v)
        {
            if (v == null || v == DBNull.Value) return 0;
            double.TryParse(Convert.ToString(v), out double d);
            return d;
        }

        private static string NormalizeSyncId(string syncId)
        {
            if (string.IsNullOrWhiteSpace(syncId)) return syncId;
            return syncId.Trim().Replace("-", "").ToLowerInvariant();
        }

        private static string WhereSyncIdEq()
        {
            return "lower(replace(trim(IFNULL(SyncId,'')), '-', '')) = @ns";
        }

        private static void BindNormSync(SQLiteCommand cmd, string syncId)
        {
            cmd.Parameters.AddWithValue("@ns", NormalizeSyncId(syncId) ?? "");
        }

        /// <summary>
        /// Cloud UUID (hyphens) vs local hex SyncId used to INSERT cloned dealer/payout rows.
        /// Collapse clones locally; pull protocol unchanged.
        /// </summary>
        private static void CollapseHyphenSyncClones(SQLiteConnection con)
        {
            BeginRemoteApply(con);
            try
            {
                CollapseDupSyncRows(con, "DieselLedgerCredit", "LedgerID");
                CollapseDupSyncRows(con, "DieselLedgerDebit", "LedgerID");
                CollapseDupSyncRows(con, "AddStock", "Sid");
                CollapseDupDealers(con);
            }
            catch { }
            finally
            {
                EndRemoteApply(con);
            }
        }

        private static void CollapseDupSyncRows(SQLiteConnection con, string table, string pk)
        {
            var groups = new Dictionary<string, List<long>>(StringComparer.Ordinal);
            using (var cmd = new SQLiteCommand(
                $"SELECT {pk}, SyncId FROM {table} WHERE SyncId IS NOT NULL AND trim(SyncId)<>''", con))
            using (var rd = cmd.ExecuteReader())
            {
                while (rd.Read())
                {
                    long id = Convert.ToInt64(rd[0]);
                    string n = NormalizeSyncId(Convert.ToString(rd[1]));
                    if (string.IsNullOrEmpty(n)) continue;
                    List<long> list;
                    if (!groups.TryGetValue(n, out list))
                    {
                        list = new List<long>();
                        groups[n] = list;
                    }
                    list.Add(id);
                }
            }
            foreach (var kv in groups)
            {
                if (kv.Value.Count < 2) continue;
                kv.Value.Sort();
                for (int i = 1; i < kv.Value.Count; i++)
                {
                    using (var d = new SQLiteCommand($"DELETE FROM {table} WHERE {pk}=@id", con))
                    {
                        d.Parameters.AddWithValue("@id", kv.Value[i]);
                        d.ExecuteNonQuery();
                    }
                }
            }
        }

        private static void CollapseDupDealers(SQLiteConnection con)
        {
            var groups = new Dictionary<string, List<int>>(StringComparer.Ordinal);
            using (var cmd = new SQLiteCommand(
                "SELECT Did, SyncId FROM AddDealer WHERE SyncId IS NOT NULL AND trim(SyncId)<>''", con))
            using (var rd = cmd.ExecuteReader())
            {
                while (rd.Read())
                {
                    int id = Convert.ToInt32(rd[0]);
                    string n = NormalizeSyncId(Convert.ToString(rd[1]));
                    if (string.IsNullOrEmpty(n)) continue;
                    List<int> list;
                    if (!groups.TryGetValue(n, out list))
                    {
                        list = new List<int>();
                        groups[n] = list;
                    }
                    list.Add(id);
                }
            }
            foreach (var kv in groups)
            {
                if (kv.Value.Count < 2) continue;
                kv.Value.Sort();
                int keep = kv.Value[0];
                for (int i = 1; i < kv.Value.Count; i++)
                {
                    int extra = kv.Value[i];
                    ExecRetarget(con, "UPDATE DieselLedgerCredit SET Did=@k WHERE Did=@e", keep, extra);
                    ExecRetarget(con, "UPDATE DieselLedgerDebit SET Did=@k WHERE Did=@e", keep, extra);
                    ExecRetarget(con, "UPDATE AddStock SET DealerId=@k WHERE DealerId=@e", keep, extra);
                    ExecRetarget(con, "UPDATE StockDiesel SET SDid=@k WHERE SDid=@e", keep, extra);
                    ExecRetarget(con, "UPDATE BankTransactions SET DealerId=@k WHERE DealerId=@e", keep, extra);
                    using (var d = new SQLiteCommand("DELETE FROM AddDealer WHERE Did=@e", con))
                    {
                        d.Parameters.AddWithValue("@e", extra);
                        d.ExecuteNonQuery();
                    }
                }
            }
        }

        private static void ExecRetarget(SQLiteConnection con, string sql, int keep, int extra)
        {
            try
            {
                using (var cmd = new SQLiteCommand(sql, con))
                {
                    cmd.Parameters.AddWithValue("@k", keep);
                    cmd.Parameters.AddWithValue("@e", extra);
                    cmd.ExecuteNonQuery();
                }
            }
            catch { }
        }

        private static object LocalIdBySync(SQLiteConnection con, string table, string pk, string syncId)
        {
            if (string.IsNullOrWhiteSpace(syncId)) return DBNull.Value;
            using (var cmd = new SQLiteCommand(
                $"SELECT {pk} FROM {table} WHERE {WhereSyncIdEq()} ORDER BY {pk} ASC LIMIT 1", con))
            {
                BindNormSync(cmd, syncId);
                var v = cmd.ExecuteScalar();
                return v ?? (object)DBNull.Value;
            }
        }

        private static bool HasTombstone(SQLiteConnection con, string syncId)
        {
            using (var cmd = new SQLiteCommand("SELECT 1 FROM SyncTombstone WHERE " + WhereSyncIdEq() + " LIMIT 1", con))
            {
                BindNormSync(cmd, syncId);
                return cmd.ExecuteScalar() != null;
            }
        }

        private static bool IsSyncDirty(SQLiteConnection con, string table, string syncId)
        {
            using (var cmd = new SQLiteCommand(
                $"SELECT SyncDirty FROM {table} WHERE {WhereSyncIdEq()} ORDER BY rowid ASC LIMIT 1", con))
            {
                BindNormSync(cmd, syncId);
                var v = cmd.ExecuteScalar();
                if (v == null || v == DBNull.Value) return false;
                return Convert.ToInt32(v) == 1;
            }
        }

        private static bool ShouldApply(SQLiteConnection con, string table, string syncId, string remoteUpdated, bool remoteDeleted, string remoteDeviceId, long? remoteServerRev = null)
        {
            if (HasTombstone(con, syncId))
            {
                // Keep local tombstone until push ack; never resurrect from stale live row.
                return remoteDeleted;
            }
            using (var cmd = new SQLiteCommand($"SELECT UpdatedAt, SyncDirty, ServerRev FROM {table} WHERE {WhereSyncIdEq()} LIMIT 1", con))
            {
                BindNormSync(cmd, syncId);
                using (var r = cmd.ExecuteReader())
                {
                    if (!r.Read()) return true;
                    int dirty = r["SyncDirty"] == DBNull.Value ? 0 : Convert.ToInt32(r["SyncDirty"]);
                    if (!_forceAuthoritativeApply && dirty == 1) return false;
                    if (_forceAuthoritativeApply) return true;

                    long? localRev = null;
                    if (r["ServerRev"] != DBNull.Value)
                    {
                        try
                        {
                            long v = Convert.ToInt64(r["ServerRev"]);
                            if (v > 0) localRev = v;
                        }
                        catch { }
                    }
                    long? remoteRev = remoteServerRev.HasValue && remoteServerRev.Value > 0 ? remoteServerRev : null;
                    if (remoteRev.HasValue)
                    {
                        if (localRev.HasValue)
                        {
                            if (remoteRev.Value > localRev.Value) return true;
                            if (remoteRev.Value < localRev.Value) return false;
                            return remoteDeleted;
                        }
                        // Local missing trusted rev — apply authoritative cloud.
                        return true;
                    }

                    DateTime local = DateTime.TryParse(Convert.ToString(r["UpdatedAt"]), out var lt) ? lt.ToUniversalTime() : DateTime.MinValue;
                    DateTime remote = DateTime.TryParse(remoteUpdated, out var rt) ? rt.ToUniversalTime() : DateTime.MinValue;
                    int cmp = remote.CompareTo(local);
                    if (cmp > 0) return true;
                    if (cmp < 0) return false;
                    if (remoteDeleted) return true;
                    string ld = _deviceId ?? "";
                    string rd = remoteDeviceId ?? "";
                    return string.CompareOrdinal(rd, ld) >= 0;
                }
            }
        }

        /// <summary>
        /// Remote soft-delete apply: local row delete must NOT leave SyncTombstone
        /// (delete triggers would re-push and create sync loops).
        /// Protects SyncDirty rows and local tombstones until upload ack.
        /// </summary>
        private static void ApplyRemoteDelete(SQLiteConnection con, string table, string syncId)
        {
            if (string.IsNullOrWhiteSpace(syncId)) return;
            if (HasTombstone(con, syncId) && !_forceAuthoritativeApply) return;
            if (!_forceAuthoritativeApply)
            {
                using (var chk = new SQLiteCommand($"SELECT SyncDirty FROM {table} WHERE {WhereSyncIdEq()} LIMIT 1", con))
                {
                    BindNormSync(chk, syncId);
                    var v = chk.ExecuteScalar();
                    if (v != null && v != DBNull.Value && Convert.ToInt32(v) == 1) return;
                }
            }
            using (var d = new SQLiteCommand($"DELETE FROM {table} WHERE {WhereSyncIdEq()}", con))
            {
                BindNormSync(d, syncId);
                d.ExecuteNonQuery();
            }
            using (var t = new SQLiteCommand("DELETE FROM SyncTombstone WHERE " + WhereSyncIdEq(), con))
            {
                BindNormSync(t, syncId);
                t.ExecuteNonQuery();
            }
        }

        private static bool DealerHasPendingChildren(SQLiteConnection con, object did)
        {
            if (did == null || did == DBNull.Value) return false;
            string[] sqls =
            {
                "SELECT 1 FROM DieselLedgerCredit WHERE Did=@id AND IFNULL(SyncDirty,1)=1 LIMIT 1",
                "SELECT 1 FROM DieselLedgerDebit WHERE Did=@id AND IFNULL(SyncDirty,1)=1 LIMIT 1",
                "SELECT 1 FROM AddStock WHERE DealerId=@id AND IFNULL(SyncDirty,1)=1 LIMIT 1",
                "SELECT 1 FROM StockDiesel WHERE SDid=@id AND IFNULL(SyncDirty,1)=1 LIMIT 1"
            };
            foreach (var sql in sqls)
            {
                using (var cmd = new SQLiteCommand(sql, con))
                {
                    cmd.Parameters.AddWithValue("@id", did);
                    if (cmd.ExecuteScalar() != null) return true;
                }
            }
            return false;
        }

        private static string ApplyCustomers(SQLiteConnection con, IEnumerable<JObject> rows)
        {
            string appliedMax = GetCheckpoint("zaib_customers");
            foreach (var r in rows)
            {
                string syncId = r.Value<string>("sync_id");
                if (string.IsNullOrWhiteSpace(syncId)) continue;
                string updated = r.Value<string>("updated_at") ?? DateTime.UtcNow.ToString("o");
                bool deleted = r["deleted_at"] != null && r["deleted_at"].Type != JTokenType.Null;
                if (!ShouldApply(con, "AddCustomer", syncId, updated, deleted, r.Value<string>("device_id")))
                {
                    if (HasTombstone(con, syncId) || IsSyncDirty(con, "AddCustomer", syncId))
                        StageRemote(con, "zaib_customers", r);
                    AdvanceCheckpoint(updated, syncId, ref appliedMax);
                    continue;
                }
                if (deleted)
                {
                    ApplyRemoteDelete(con, "AddCustomer", syncId);
                    AdvanceCheckpoint(updated, syncId, ref appliedMax);
                    continue;
                }
                using (var chk = new SQLiteCommand("SELECT id FROM AddCustomer WHERE SyncId=@s", con))
                {
                    chk.Parameters.AddWithValue("@s", syncId);
                    var exists = chk.ExecuteScalar();
                    if (exists == null)
                    {
                        using (var i = new SQLiteCommand("INSERT INTO AddCustomer(Name,Mobile,Date,SyncId,UpdatedAt,SyncDirty) VALUES(@n,@m,@d,@s,@u,0)", con))
                        {
                            i.Parameters.AddWithValue("@n", r.Value<string>("name") ?? "");
                            i.Parameters.AddWithValue("@m", r.Value<string>("mobile") ?? "");
                            i.Parameters.AddWithValue("@d", r.Value<string>("date_text") ?? "");
                            i.Parameters.AddWithValue("@s", syncId);
                            i.Parameters.AddWithValue("@u", updated);
                            i.ExecuteNonQuery();
                        }
                    }
                    else
                    {
                        using (var u = new SQLiteCommand("UPDATE AddCustomer SET Name=@n, Mobile=@m, Date=@d, UpdatedAt=@u, SyncDirty=0 WHERE SyncId=@s", con))
                        {
                            u.Parameters.AddWithValue("@n", r.Value<string>("name") ?? "");
                            u.Parameters.AddWithValue("@m", r.Value<string>("mobile") ?? "");
                            u.Parameters.AddWithValue("@d", r.Value<string>("date_text") ?? "");
                            u.Parameters.AddWithValue("@u", updated);
                            u.Parameters.AddWithValue("@s", syncId);
                            u.ExecuteNonQuery();
                        }
                    }
                }
                AdvanceCheckpoint(updated, syncId, ref appliedMax);
            }
            return appliedMax;
        }

        private static string ApplyDealers(SQLiteConnection con, IEnumerable<JObject> rows)
        {
            string appliedMax = GetCheckpoint("zaib_dealers");
            foreach (var r in rows)
            {
                string syncId = r.Value<string>("sync_id");
                if (string.IsNullOrWhiteSpace(syncId)) continue;
                string updated = r.Value<string>("updated_at") ?? DateTime.UtcNow.ToString("o");
                bool deleted = r["deleted_at"] != null && r["deleted_at"].Type != JTokenType.Null;
                if (!ShouldApply(con, "AddDealer", syncId, updated, deleted, r.Value<string>("device_id")))
                {
                    if (HasTombstone(con, syncId) || IsSyncDirty(con, "AddDealer", syncId))
                        StageRemote(con, "zaib_dealers", r);
                    AdvanceCheckpoint(updated, syncId, ref appliedMax);
                    continue;
                }
                if (deleted)
                {
                    ApplyRemoteDelete(con, "AddDealer", syncId);
                    AdvanceCheckpoint(updated, syncId, ref appliedMax);
                    continue;
                }
                using (var chk = new SQLiteCommand("SELECT Did FROM AddDealer WHERE " + WhereSyncIdEq() + " ORDER BY Did ASC LIMIT 1", con))
                {
                    BindNormSync(chk, syncId);
                    var exists = chk.ExecuteScalar();
                    if (exists == null)
                    {
                        using (var i = new SQLiteCommand("INSERT INTO AddDealer(DealerName,DDAmount,DAmount,Date,SyncId,UpdatedAt,SyncDirty) VALUES(@n,0,0,@dt,@s,@u,0)", con))
                        {
                            i.Parameters.AddWithValue("@n", r.Value<string>("dealer_name") ?? "");
                            i.Parameters.AddWithValue("@dt", r.Value<string>("date_text") ?? "");
                            i.Parameters.AddWithValue("@s", syncId);
                            i.Parameters.AddWithValue("@u", updated);
                            i.ExecuteNonQuery();
                        }
                    }
                    else
                    {
                        using (var u = new SQLiteCommand("UPDATE AddDealer SET DealerName=@n, Date=@dt, UpdatedAt=@u, SyncDirty=0 WHERE " + WhereSyncIdEq(), con))
                        {
                            u.Parameters.AddWithValue("@n", r.Value<string>("dealer_name") ?? "");
                            u.Parameters.AddWithValue("@dt", r.Value<string>("date_text") ?? "");
                            u.Parameters.AddWithValue("@u", updated);
                            BindNormSync(u, syncId);
                            u.ExecuteNonQuery();
                        }
                    }
                }
                AdvanceCheckpoint(updated, syncId, ref appliedMax);
            }
            return appliedMax;
        }

        private static string ApplyPetrol(SQLiteConnection con, IEnumerable<JObject> rows)
        {
            string appliedMax = GetCheckpoint("zaib_petrol_entries");
            foreach (var r in rows)
            {
                string syncId = r.Value<string>("sync_id");
                if (string.IsNullOrWhiteSpace(syncId)) continue;
                string updated = r.Value<string>("updated_at") ?? DateTime.UtcNow.ToString("o");
                bool deleted = r["deleted_at"] != null && r["deleted_at"].Type != JTokenType.Null;
                if (!ShouldApply(con, "PetrolAdd", syncId, updated, deleted, r.Value<string>("device_id")))
                {
                    if (HasTombstone(con, syncId) || IsSyncDirty(con, "PetrolAdd", syncId))
                        StageRemote(con, "zaib_petrol_entries", r);
                    AdvanceCheckpoint(updated, syncId, ref appliedMax);
                    continue;
                }
                if (deleted)
                {
                    ApplyRemoteDelete(con, "PetrolAdd", syncId);
                    AdvanceCheckpoint(updated, syncId, ref appliedMax);
                    continue;
                }
                string custSync = r.Value<string>("customer_sync_id");
                object custId = LocalIdBySync(con, "AddCustomer", "id", custSync);
                if (!string.IsNullOrWhiteSpace(custSync) && (custId == null || custId == DBNull.Value))
                {
                    StageRemote(con, "zaib_petrol_entries", r);
                    continue;
                }
                using (var chk = new SQLiteCommand("SELECT pid FROM PetrolAdd WHERE SyncId=@s", con))
                {
                    chk.Parameters.AddWithValue("@s", syncId);
                    var exists = chk.ExecuteScalar();
                    string sql = exists == null
                        ? "INSERT INTO PetrolAdd(Date,ReceiptNo,vehicle,Litter,Rate,Advance,Amount,Credit,Balance,Note,CustomerId,Processed,IsInitialEntry,SyncId,UpdatedAt,SyncDirty) VALUES(@Date,@ReceiptNo,@vehicle,@Litter,@Rate,@Advance,@Amount,@Credit,@Balance,@Note,@CustomerId,@Processed,@IsInitialEntry,@s,@u,0)"
                        : "UPDATE PetrolAdd SET Date=@Date,ReceiptNo=@ReceiptNo,vehicle=@vehicle,Litter=@Litter,Rate=@Rate,Advance=@Advance,Amount=@Amount,Credit=@Credit,Balance=@Balance,Note=@Note,CustomerId=@CustomerId,Processed=@Processed,IsInitialEntry=@IsInitialEntry,UpdatedAt=@u,SyncDirty=0 WHERE SyncId=@s";
                    using (var cmd = new SQLiteCommand(sql, con))
                    {
                        cmd.Parameters.AddWithValue("@Date", r.Value<string>("date_text") ?? "");
                        cmd.Parameters.AddWithValue("@ReceiptNo", r.Value<string>("receipt_no") ?? "");
                        cmd.Parameters.AddWithValue("@vehicle", r.Value<string>("vehicle") ?? "");
                        cmd.Parameters.AddWithValue("@Litter", r.Value<double?>("litter") ?? 0);
                        cmd.Parameters.AddWithValue("@Rate", r.Value<double?>("rate") ?? 0);
                        cmd.Parameters.AddWithValue("@Advance", r.Value<double?>("advance") ?? 0);
                        cmd.Parameters.AddWithValue("@Amount", r.Value<double?>("amount") ?? 0);
                        cmd.Parameters.AddWithValue("@Credit", r.Value<double?>("credit") ?? 0);
                        cmd.Parameters.AddWithValue("@Balance", r.Value<double?>("balance") ?? 0);
                        cmd.Parameters.AddWithValue("@Note", r.Value<string>("note") ?? "");
                        cmd.Parameters.AddWithValue("@CustomerId", custId);
                        cmd.Parameters.AddWithValue("@Processed", r.Value<int?>("processed") ?? 0);
                        cmd.Parameters.AddWithValue("@IsInitialEntry", r.Value<int?>("is_initial_entry") ?? 1);
                        cmd.Parameters.AddWithValue("@s", syncId);
                        cmd.Parameters.AddWithValue("@u", updated);
                        cmd.ExecuteNonQuery();
                    }
                }
                AdvanceCheckpoint(updated, syncId, ref appliedMax);
            }
            return appliedMax;
        }

        private static string ApplyPayouts(SQLiteConnection con, IEnumerable<JObject> rows)
        {
            string appliedMax = GetCheckpoint("zaib_dealer_payouts");
            foreach (var r in rows)
            {
                bool applied = UpsertChild(con, "DieselLedgerCredit", "LedgerID", "zaib_dealer_payouts", r,
                    (cmd, row, did) =>
                    {
                        cmd.Parameters.AddWithValue("@Did", did);
                        cmd.Parameters.AddWithValue("@Date", row.Value<string>("date_text") ?? "");
                        cmd.Parameters.AddWithValue("@AmounGiven", row.Value<double?>("amount_given") ?? 0);
                        cmd.Parameters.AddWithValue("@Note", row.Value<string>("note") ?? "");
                    },
                    "Did,Date,AmounGiven,Note",
                    "AddDealer", "Did", "dealer_sync_id");
                if (applied)
                    AdvanceCheckpoint(r.Value<string>("updated_at"), r.Value<string>("sync_id") ?? "", ref appliedMax);
            }
            return appliedMax;
        }

        private static string ApplyPurchases(SQLiteConnection con, IEnumerable<JObject> rows)
        {
            string appliedMax = GetCheckpoint("zaib_dealer_purchases");
            foreach (var r in rows)
            {
                string syncId = r.Value<string>("sync_id");
                if (string.IsNullOrWhiteSpace(syncId)) continue;
                string updated = r.Value<string>("updated_at") ?? DateTime.UtcNow.ToString("o");
                bool deleted = r["deleted_at"] != null && r["deleted_at"].Type != JTokenType.Null;
                if (!ShouldApply(con, "AddStock", syncId, updated, deleted, r.Value<string>("device_id")))
                {
                    if (HasTombstone(con, syncId) || IsSyncDirty(con, "AddStock", syncId))
                        StageRemote(con, "zaib_dealer_purchases", r);
                    AdvanceCheckpoint(updated, syncId, ref appliedMax);
                    continue;
                }
                if (deleted)
                {
                    ReverseDealerBalance(con, syncId);
                    ApplyRemoteDelete(con, "AddStock", syncId);
                    AdvanceCheckpoint(updated, syncId, ref appliedMax);
                    continue;
                }
                string dSync = r.Value<string>("dealer_sync_id");
                object did = LocalIdBySync(con, "AddDealer", "Did", dSync);
                if (!string.IsNullOrWhiteSpace(dSync) && (did == null || did == DBNull.Value))
                {
                    StageRemote(con, "zaib_dealer_purchases", r);
                    continue;
                }
                using (var chk = new SQLiteCommand("SELECT Sid FROM AddStock WHERE " + WhereSyncIdEq() + " LIMIT 1", con))
                {
                    BindNormSync(chk, syncId);
                    var exists = chk.ExecuteScalar();
                    string sql = exists == null
                        ? "INSERT INTO AddStock(Vehicle,Rate,SellDisel,Stock,Date,AddDisel,DealerId,Note,SyncId,UpdatedAt,SyncDirty) VALUES(@Vehicle,@Rate,0,0,@Date,@AddDisel,@DealerId,@Note,@s,@u,0)"
                        : "UPDATE AddStock SET Vehicle=@Vehicle,Rate=@Rate,Date=@Date,AddDisel=@AddDisel,DealerId=@DealerId,Note=@Note,UpdatedAt=@u,SyncDirty=0 WHERE " + WhereSyncIdEq();
                    using (var cmd = new SQLiteCommand(sql, con))
                    {
                        cmd.Parameters.AddWithValue("@Vehicle", r.Value<string>("vehicle") ?? "");
                        cmd.Parameters.AddWithValue("@Rate", r.Value<double?>("rate") ?? 0);
                        cmd.Parameters.AddWithValue("@Date", r.Value<string>("date_text") ?? "");
                        cmd.Parameters.AddWithValue("@AddDisel", r.Value<double?>("add_diesel") ?? 0);
                        cmd.Parameters.AddWithValue("@DealerId", did);
                        cmd.Parameters.AddWithValue("@Note", r.Value<string>("note") ?? "");
                        cmd.Parameters.AddWithValue("@s", syncId);
                        cmd.Parameters.AddWithValue("@u", updated);
                        if (exists != null)
                            BindNormSync(cmd, syncId);
                        cmd.ExecuteNonQuery();
                    }
                }
                double addDiesel = r.Value<double?>("add_diesel") ?? 0;
                double rate = r.Value<double?>("rate") ?? 0;
                ReconcileDealerBalance(con, syncId, did, addDiesel * rate, 0);
                AdvanceCheckpoint(updated, syncId, ref appliedMax);
            }
            return appliedMax;
        }

        private static string ApplyDirect(SQLiteConnection con, IEnumerable<JObject> rows)
        {
            string appliedMax = GetCheckpoint("zaib_dealer_direct");
            foreach (var r in rows)
            {
                bool applied = UpsertChild(con, "DieselLedgerDebit", "LedgerID", "zaib_dealer_direct", r,
                    (cmd, row, did) =>
                    {
                        cmd.Parameters.AddWithValue("@Did", did);
                        cmd.Parameters.AddWithValue("@Date", row.Value<string>("date_text") ?? "");
                        cmd.Parameters.AddWithValue("@AmounGiven", row.Value<double?>("amount_given") ?? 0);
                        cmd.Parameters.AddWithValue("@Note", row.Value<string>("note") ?? "");
                    },
                    "Did,Date,AmounGiven,Note",
                    "AddDealer", "Did", "dealer_sync_id");
                if (applied)
                    AdvanceCheckpoint(r.Value<string>("updated_at"), r.Value<string>("sync_id") ?? "", ref appliedMax);
            }
            return appliedMax;
        }

        private static string ApplyStock(SQLiteConnection con, IEnumerable<JObject> rows)
        {
            string appliedMax = GetCheckpoint("zaib_stock_diesel");
            foreach (var r in rows)
            {
                string syncId = r.Value<string>("sync_id");
                if (string.IsNullOrWhiteSpace(syncId)) continue;
                string updated = r.Value<string>("updated_at") ?? DateTime.UtcNow.ToString("o");
                bool deleted = r["deleted_at"] != null && r["deleted_at"].Type != JTokenType.Null;
                if (!ShouldApply(con, "StockDiesel", syncId, updated, deleted, r.Value<string>("device_id")))
                {
                    if (HasTombstone(con, syncId) || IsSyncDirty(con, "StockDiesel", syncId))
                        StageRemote(con, "zaib_stock_diesel", r);
                    AdvanceCheckpoint(updated, syncId, ref appliedMax);
                    continue;
                }
                if (deleted)
                {
                    ApplyRemoteDelete(con, "StockDiesel", syncId);
                    AdvanceCheckpoint(updated, syncId, ref appliedMax);
                    continue;
                }
                string dSync = r.Value<string>("dealer_sync_id");
                object did = LocalIdBySync(con, "AddDealer", "Did", dSync);
                if (!string.IsNullOrWhiteSpace(dSync) && (did == null || did == DBNull.Value))
                {
                    StageRemote(con, "zaib_stock_diesel", r);
                    continue;
                }
                using (var chk = new SQLiteCommand("SELECT SID FROM StockDiesel WHERE SyncId=@s", con))
                {
                    chk.Parameters.AddWithValue("@s", syncId);
                    var exists = chk.ExecuteScalar();
                    string sql = exists == null
                        ? "INSERT INTO StockDiesel(SDid,Date,Vehicle,Litter,Rate,Credit,Debit,Note,SyncId,UpdatedAt,SyncDirty) VALUES(@SDid,@Date,@Vehicle,@Litter,@Rate,@Credit,@Debit,@Note,@s,@u,0)"
                        : "UPDATE StockDiesel SET SDid=@SDid,Date=@Date,Vehicle=@Vehicle,Litter=@Litter,Rate=@Rate,Credit=@Credit,Debit=@Debit,Note=@Note,UpdatedAt=@u,SyncDirty=0 WHERE SyncId=@s";
                    using (var cmd = new SQLiteCommand(sql, con))
                    {
                        cmd.Parameters.AddWithValue("@SDid", did);
                        cmd.Parameters.AddWithValue("@Date", r.Value<string>("date_text") ?? "");
                        cmd.Parameters.AddWithValue("@Vehicle", r.Value<string>("vehicle") ?? "");
                        cmd.Parameters.AddWithValue("@Litter", r.Value<double?>("litter") ?? 0);
                        cmd.Parameters.AddWithValue("@Rate", r.Value<double?>("rate") ?? 0);
                        cmd.Parameters.AddWithValue("@Credit", r.Value<double?>("credit") ?? 0);
                        cmd.Parameters.AddWithValue("@Debit", r.Value<double?>("debit") ?? 0);
                        cmd.Parameters.AddWithValue("@Note", r.Value<string>("note") ?? "");
                        cmd.Parameters.AddWithValue("@s", syncId);
                        cmd.Parameters.AddWithValue("@u", updated);
                        cmd.ExecuteNonQuery();
                    }
                }
                AdvanceCheckpoint(updated, syncId, ref appliedMax);
            }
            return appliedMax;
        }

        private static string ApplyBank(SQLiteConnection con, IEnumerable<JObject> rows)
        {
            string appliedMax = GetCheckpoint("zaib_bank_transactions");
            foreach (var r in rows)
            {
                string syncId = r.Value<string>("sync_id");
                if (string.IsNullOrWhiteSpace(syncId)) continue;
                string updated = r.Value<string>("updated_at") ?? DateTime.UtcNow.ToString("o");
                bool deleted = r["deleted_at"] != null && r["deleted_at"].Type != JTokenType.Null;
                if (!ShouldApply(con, "BankTransactions", syncId, updated, deleted, r.Value<string>("device_id")))
                {
                    if (HasTombstone(con, syncId) || IsSyncDirty(con, "BankTransactions", syncId))
                        StageRemote(con, "zaib_bank_transactions", r);
                    AdvanceCheckpoint(updated, syncId, ref appliedMax);
                    continue;
                }
                if (deleted)
                {
                    ApplyRemoteDelete(con, "BankTransactions", syncId);
                    AdvanceCheckpoint(updated, syncId, ref appliedMax);
                    continue;
                }
                string cSync = r.Value<string>("customer_sync_id");
                string dSync = r.Value<string>("dealer_sync_id");
                object cid = LocalIdBySync(con, "AddCustomer", "id", cSync);
                object did = LocalIdBySync(con, "AddDealer", "Did", dSync);
                if ((!string.IsNullOrWhiteSpace(cSync) && (cid == null || cid == DBNull.Value)) ||
                    (!string.IsNullOrWhiteSpace(dSync) && (did == null || did == DBNull.Value)))
                {
                    StageRemote(con, "zaib_bank_transactions", r);
                    continue;
                }
                using (var chk = new SQLiteCommand("SELECT Id FROM BankTransactions WHERE SyncId=@s", con))
                {
                    chk.Parameters.AddWithValue("@s", syncId);
                    var exists = chk.ExecuteScalar();
                    string sql = exists == null
                        ? "INSERT INTO BankTransactions(TransactionDate,TransactionType,CustomerId,DealerId,Amount,Note,BankName,SyncId,UpdatedAt,SyncDirty) VALUES(@TransactionDate,@TransactionType,@CustomerId,@DealerId,@Amount,@Note,@BankName,@s,@u,0)"
                        : "UPDATE BankTransactions SET TransactionDate=@TransactionDate,TransactionType=@TransactionType,CustomerId=@CustomerId,DealerId=@DealerId,Amount=@Amount,Note=@Note,BankName=@BankName,UpdatedAt=@u,SyncDirty=0 WHERE SyncId=@s";
                    using (var cmd = new SQLiteCommand(sql, con))
                    {
                        cmd.Parameters.AddWithValue("@TransactionDate", r.Value<string>("transaction_date") ?? "");
                        cmd.Parameters.AddWithValue("@TransactionType", r.Value<string>("transaction_type") ?? "");
                        cmd.Parameters.AddWithValue("@CustomerId", cid);
                        cmd.Parameters.AddWithValue("@DealerId", did);
                        cmd.Parameters.AddWithValue("@Amount", r.Value<double?>("amount") ?? 0);
                        cmd.Parameters.AddWithValue("@Note", r.Value<string>("note") ?? "");
                        cmd.Parameters.AddWithValue("@BankName", r.Value<string>("bank_name") ?? "");
                        cmd.Parameters.AddWithValue("@s", syncId);
                        cmd.Parameters.AddWithValue("@u", updated);
                        cmd.ExecuteNonQuery();
                    }
                }
                AdvanceCheckpoint(updated, syncId, ref appliedMax);
            }
            return appliedMax;
        }

        private static string ApplyExpenses(SQLiteConnection con, IEnumerable<JObject> rows)
        {
            if (!TableExists(con, "Expensetable")) return GetCheckpoint("zaib_expenses");
            string appliedMax = GetCheckpoint("zaib_expenses");
            foreach (var r in rows)
            {
                string syncId = r.Value<string>("sync_id");
                if (string.IsNullOrWhiteSpace(syncId)) continue;
                string updated = r.Value<string>("updated_at") ?? DateTime.UtcNow.ToString("o");
                bool deleted = r["deleted_at"] != null && r["deleted_at"].Type != JTokenType.Null;
                if (!ShouldApply(con, "Expensetable", syncId, updated, deleted, r.Value<string>("device_id")))
                {
                    if (HasTombstone(con, syncId) || IsSyncDirty(con, "Expensetable", syncId))
                        StageRemote(con, "zaib_expenses", r);
                    AdvanceCheckpoint(updated, syncId, ref appliedMax);
                    continue;
                }
                if (deleted)
                {
                    ApplyRemoteDelete(con, "Expensetable", syncId);
                    AdvanceCheckpoint(updated, syncId, ref appliedMax);
                    continue;
                }
                using (var chk = new SQLiteCommand("SELECT sid FROM Expensetable WHERE SyncId=@s", con))
                {
                    chk.Parameters.AddWithValue("@s", syncId);
                    var exists = chk.ExecuteScalar();
                    string sql = exists == null
                        ? "INSERT INTO Expensetable(Name,Category,Amount,EDate,Note,SyncId,UpdatedAt,SyncDirty) VALUES(@Name,@Category,@Amount,@EDate,@Note,@s,@u,0)"
                        : "UPDATE Expensetable SET Name=@Name,Category=@Category,Amount=@Amount,EDate=@EDate,Note=@Note,UpdatedAt=@u,SyncDirty=0 WHERE SyncId=@s";
                    using (var cmd = new SQLiteCommand(sql, con))
                    {
                        cmd.Parameters.AddWithValue("@Name", r.Value<string>("name") ?? "");
                        cmd.Parameters.AddWithValue("@Category", r.Value<string>("category") ?? "");
                        cmd.Parameters.AddWithValue("@Amount", r.Value<double?>("amount") ?? 0);
                        cmd.Parameters.AddWithValue("@EDate", r.Value<string>("e_date") ?? "");
                        cmd.Parameters.AddWithValue("@Note", r.Value<string>("note") ?? "");
                        cmd.Parameters.AddWithValue("@s", syncId);
                        cmd.Parameters.AddWithValue("@u", updated);
                        cmd.ExecuteNonQuery();
                    }
                }
                AdvanceCheckpoint(updated, syncId, ref appliedMax);
            }
            return appliedMax;
        }

        private static void FlushStaged(SQLiteConnection con)
        {
            if (!TableExists(con, "SyncStagedRemote")) return;
            var dt = QueryDirty(con, "SELECT * FROM SyncStagedRemote ORDER BY UpdatedAt ASC");
            foreach (DataRow row in dt.Rows)
            {
                string cloud = Convert.ToString(row["CloudTable"]);
                string syncId = Convert.ToString(row["SyncId"]);
                try
                {
                    var payload = JObject.Parse(Convert.ToString(row["PayloadJson"]) ?? "{}");
                    bool applied = false;
                    if (cloud == "zaib_petrol_entries")
                        applied = ApplyOnePetrol(con, payload,
                            payload["deleted_at"] != null && payload["deleted_at"].Type != JTokenType.Null);
                    else if (cloud == "zaib_dealer_payouts")
                    {
                        applied = UpsertChild(con, "DieselLedgerCredit", "LedgerID", cloud, payload,
                            (cmd, row2, did) =>
                            {
                                cmd.Parameters.AddWithValue("@Did", did);
                                cmd.Parameters.AddWithValue("@Date", row2.Value<string>("date_text") ?? "");
                                cmd.Parameters.AddWithValue("@AmounGiven", row2.Value<double?>("amount_given") ?? 0);
                                cmd.Parameters.AddWithValue("@Note", row2.Value<string>("note") ?? "");
                            },
                            "Did,Date,AmounGiven,Note", "AddDealer", "Did", "dealer_sync_id");
                    }
                    else if (cloud == "zaib_dealer_direct")
                    {
                        applied = UpsertChild(con, "DieselLedgerDebit", "LedgerID", cloud, payload,
                            (cmd, row2, did) =>
                            {
                                cmd.Parameters.AddWithValue("@Did", did);
                                cmd.Parameters.AddWithValue("@Date", row2.Value<string>("date_text") ?? "");
                                cmd.Parameters.AddWithValue("@AmounGiven", row2.Value<double?>("amount_given") ?? 0);
                                cmd.Parameters.AddWithValue("@Note", row2.Value<string>("note") ?? "");
                            },
                            "Did,Date,AmounGiven,Note", "AddDealer", "Did", "dealer_sync_id");
                    }
                    else if (cloud == "zaib_dealer_purchases")
                    {
                        string updated = payload.Value<string>("updated_at") ?? DateTime.UtcNow.ToString("o");
                        bool deleted = payload["deleted_at"] != null && payload["deleted_at"].Type != JTokenType.Null;
                        if (!ShouldApply(con, "AddStock", syncId, updated, deleted, payload.Value<string>("device_id")))
                        {
                            applied = false;
                        }
                        else if (deleted) { ReverseDealerBalance(con, syncId); ApplyRemoteDelete(con, "AddStock", syncId); applied = true; }
                        else
                        {
                            string dSync = payload.Value<string>("dealer_sync_id");
                            object did = LocalIdBySync(con, "AddDealer", "Did", dSync);
                            if (!string.IsNullOrWhiteSpace(dSync) && (did == null || did == DBNull.Value))
                                applied = false;
                            else
                            {
                                using (var chk = new SQLiteCommand("SELECT Sid FROM AddStock WHERE " + WhereSyncIdEq() + " LIMIT 1", con))
                                {
                                    BindNormSync(chk, syncId);
                                    var exists = chk.ExecuteScalar();
                                    string sql = exists == null
                                        ? "INSERT INTO AddStock(Vehicle,Rate,SellDisel,Stock,Date,AddDisel,DealerId,Note,SyncId,UpdatedAt,SyncDirty) VALUES(@Vehicle,@Rate,0,0,@Date,@AddDisel,@DealerId,@Note,@s,@u,0)"
                                        : "UPDATE AddStock SET Vehicle=@Vehicle,Rate=@Rate,Date=@Date,AddDisel=@AddDisel,DealerId=@DealerId,Note=@Note,UpdatedAt=@u,SyncDirty=0 WHERE " + WhereSyncIdEq();
                                    using (var cmd = new SQLiteCommand(sql, con))
                                    {
                                        cmd.Parameters.AddWithValue("@Vehicle", payload.Value<string>("vehicle") ?? "");
                                        cmd.Parameters.AddWithValue("@Rate", payload.Value<double?>("rate") ?? 0);
                                        cmd.Parameters.AddWithValue("@Date", payload.Value<string>("date_text") ?? "");
                                        cmd.Parameters.AddWithValue("@AddDisel", payload.Value<double?>("add_diesel") ?? 0);
                                        cmd.Parameters.AddWithValue("@DealerId", did ?? (object)DBNull.Value);
                                        cmd.Parameters.AddWithValue("@Note", payload.Value<string>("note") ?? "");
                                        cmd.Parameters.AddWithValue("@s", syncId);
                                        cmd.Parameters.AddWithValue("@u", updated);
                                        if (exists != null)
                                            BindNormSync(cmd, syncId);
                                        cmd.ExecuteNonQuery();
                                    }
                                }
                                double addDiesel = payload.Value<double?>("add_diesel") ?? 0;
                                double rate = payload.Value<double?>("rate") ?? 0;
                                ReconcileDealerBalance(con, syncId, did, addDiesel * rate, 0);
                                applied = true;
                            }
                        }
                    }
                    else if (cloud == "zaib_dealer_balance_ops")
                    {
                        string updated = payload.Value<string>("updated_at") ?? DateTime.UtcNow.ToString("o");
                        bool deleted = payload["deleted_at"] != null && payload["deleted_at"].Type != JTokenType.Null;
                        applied = ApplyDealerBalanceOpRow(con, payload, updated, deleted, out _);
                    }
                    else if (cloud == "zaib_dealer_transfers")
                    {
                        applied = ApplyOneDealerTransfer(con, payload,
                            payload["deleted_at"] != null && payload["deleted_at"].Type != JTokenType.Null);
                    }
                    else if (cloud == "zaib_stock_diesel")
                    {
                        string updated = payload.Value<string>("updated_at") ?? DateTime.UtcNow.ToString("o");
                        bool deleted = payload["deleted_at"] != null && payload["deleted_at"].Type != JTokenType.Null;
                        if (!ShouldApply(con, "StockDiesel", syncId, updated, deleted, payload.Value<string>("device_id")))
                            applied = false;
                        else if (deleted) { ApplyRemoteDelete(con, "StockDiesel", syncId); applied = true; }
                        else
                        {
                            string dSync = payload.Value<string>("dealer_sync_id");
                            object did = LocalIdBySync(con, "AddDealer", "Did", dSync);
                            if (!string.IsNullOrWhiteSpace(dSync) && (did == null || did == DBNull.Value))
                                applied = false;
                            else
                            {
                                using (var chk = new SQLiteCommand("SELECT SID FROM StockDiesel WHERE SyncId=@s", con))
                                {
                                    chk.Parameters.AddWithValue("@s", syncId);
                                    var exists = chk.ExecuteScalar();
                                    string sql = exists == null
                                        ? "INSERT INTO StockDiesel(SDid,Date,Vehicle,Litter,Rate,Credit,Debit,Note,SyncId,UpdatedAt,SyncDirty) VALUES(@SDid,@Date,@Vehicle,@Litter,@Rate,@Credit,@Debit,@Note,@s,@u,0)"
                                        : "UPDATE StockDiesel SET SDid=@SDid,Date=@Date,Vehicle=@Vehicle,Litter=@Litter,Rate=@Rate,Credit=@Credit,Debit=@Debit,Note=@Note,UpdatedAt=@u,SyncDirty=0 WHERE SyncId=@s";
                                    using (var cmd = new SQLiteCommand(sql, con))
                                    {
                                        cmd.Parameters.AddWithValue("@SDid", did ?? (object)DBNull.Value);
                                        cmd.Parameters.AddWithValue("@Date", payload.Value<string>("date_text") ?? "");
                                        cmd.Parameters.AddWithValue("@Vehicle", payload.Value<string>("vehicle") ?? "");
                                        cmd.Parameters.AddWithValue("@Litter", payload.Value<double?>("litter") ?? 0);
                                        cmd.Parameters.AddWithValue("@Rate", payload.Value<double?>("rate") ?? 0);
                                        cmd.Parameters.AddWithValue("@Credit", payload.Value<double?>("credit") ?? 0);
                                        cmd.Parameters.AddWithValue("@Debit", payload.Value<double?>("debit") ?? 0);
                                        cmd.Parameters.AddWithValue("@Note", payload.Value<string>("note") ?? "");
                                        cmd.Parameters.AddWithValue("@s", syncId);
                                        cmd.Parameters.AddWithValue("@u", updated);
                                        cmd.ExecuteNonQuery();
                                    }
                                }
                                applied = true;
                            }
                        }
                    }
                    else if (cloud == "zaib_bank_transactions")
                    {
                        string updated = payload.Value<string>("updated_at") ?? DateTime.UtcNow.ToString("o");
                        bool deleted = payload["deleted_at"] != null && payload["deleted_at"].Type != JTokenType.Null;
                        if (!ShouldApply(con, "BankTransactions", syncId, updated, deleted, payload.Value<string>("device_id")))
                            applied = false;
                        else if (deleted) { ApplyRemoteDelete(con, "BankTransactions", syncId); applied = true; }
                        else
                        {
                            string cSync = payload.Value<string>("customer_sync_id");
                            string dSync = payload.Value<string>("dealer_sync_id");
                            object cid = LocalIdBySync(con, "AddCustomer", "id", cSync);
                            object did = LocalIdBySync(con, "AddDealer", "Did", dSync);
                            if ((!string.IsNullOrWhiteSpace(cSync) && (cid == null || cid == DBNull.Value)) ||
                                (!string.IsNullOrWhiteSpace(dSync) && (did == null || did == DBNull.Value)))
                                applied = false;
                            else
                            {
                                using (var chk = new SQLiteCommand("SELECT Id FROM BankTransactions WHERE SyncId=@s", con))
                                {
                                    chk.Parameters.AddWithValue("@s", syncId);
                                    var exists = chk.ExecuteScalar();
                                    string sql = exists == null
                                        ? "INSERT INTO BankTransactions(TransactionDate,TransactionType,CustomerId,DealerId,Amount,Note,BankName,SyncId,UpdatedAt,SyncDirty) VALUES(@TransactionDate,@TransactionType,@CustomerId,@DealerId,@Amount,@Note,@BankName,@s,@u,0)"
                                        : "UPDATE BankTransactions SET TransactionDate=@TransactionDate,TransactionType=@TransactionType,CustomerId=@CustomerId,DealerId=@DealerId,Amount=@Amount,Note=@Note,BankName=@BankName,UpdatedAt=@u,SyncDirty=0 WHERE SyncId=@s";
                                    using (var cmd = new SQLiteCommand(sql, con))
                                    {
                                        cmd.Parameters.AddWithValue("@TransactionDate", payload.Value<string>("transaction_date") ?? "");
                                        cmd.Parameters.AddWithValue("@TransactionType", payload.Value<string>("transaction_type") ?? "");
                                        cmd.Parameters.AddWithValue("@CustomerId", cid ?? (object)DBNull.Value);
                                        cmd.Parameters.AddWithValue("@DealerId", did ?? (object)DBNull.Value);
                                        cmd.Parameters.AddWithValue("@Amount", payload.Value<double?>("amount") ?? 0);
                                        cmd.Parameters.AddWithValue("@Note", payload.Value<string>("note") ?? "");
                                        cmd.Parameters.AddWithValue("@BankName", payload.Value<string>("bank_name") ?? "");
                                        cmd.Parameters.AddWithValue("@s", syncId);
                                        cmd.Parameters.AddWithValue("@u", updated);
                                        cmd.ExecuteNonQuery();
                                    }
                                }
                                applied = true;
                            }
                        }
                    }
                    else if (cloud == "zaib_customers" || cloud == "zaib_dealers" || cloud == "zaib_expenses")
                    {
                        // Re-queue via StageRemote keep — Flush will retry next pass after parents ready.
                        LogFail(con, "flush:deferred:" + cloud, syncId, new Exception("parent/table flush deferred — kept staged"));
                        applied = false;
                    }
                    else
                    {
                        LogFail(con, "flush:unknown:" + cloud, syncId, new Exception("unknown staged table — kept"));
                        applied = false;
                    }
                    if (applied)
                    {
                        using (var d = new SQLiteCommand("DELETE FROM SyncStagedRemote WHERE CloudTable=@t AND SyncId=@s", con))
                        {
                            d.Parameters.AddWithValue("@t", cloud);
                            d.Parameters.AddWithValue("@s", syncId);
                            d.ExecuteNonQuery();
                        }
                    }
                }
                catch (Exception ex) { LogFail(con, "flush:" + cloud, syncId, ex); }
            }
        }

        private static string ApplyDealerBalanceOps(SQLiteConnection con, IEnumerable<JObject> rows)
        {
            string appliedMax = GetCheckpoint("zaib_dealer_balance_ops");
            foreach (var r in rows)
            {
                string syncId = r.Value<string>("sync_id");
                if (string.IsNullOrWhiteSpace(syncId)) continue;
                string updated = r.Value<string>("updated_at") ?? DateTime.UtcNow.ToString("o");
                bool deleted = r["deleted_at"] != null && r["deleted_at"].Type != JTokenType.Null;
                ApplyDealerBalanceOpRow(con, r, updated, deleted, out bool advanceCp);
                if (advanceCp)
                    AdvanceCheckpoint(updated, syncId, ref appliedMax);
            }
            return appliedMax;
        }

        private static bool ApplyDealerBalanceOpRow(SQLiteConnection con, JObject r, string updated, bool deleted, out bool advanceCheckpoint)
        {
            advanceCheckpoint = true;
            string syncId = r.Value<string>("sync_id");
            if (string.IsNullOrWhiteSpace(syncId)) return true;
            if (!ShouldApply(con, "SyncDealerBalanceOp", syncId, updated, deleted, r.Value<string>("device_id")))
            {
                if (HasTombstone(con, syncId) || IsSyncDirty(con, "SyncDealerBalanceOp", syncId))
                    StageRemote(con, "zaib_dealer_balance_ops", r);
                return false;
            }
            string sourceSyncId = r.Value<string>("source_sync_id");
            if (string.IsNullOrWhiteSpace(sourceSyncId)) sourceSyncId = syncId;
            if (deleted)
            {
                ReverseDealerBalance(con, sourceSyncId);
                using (var u = new SQLiteCommand(
                    "UPDATE SyncDealerBalanceOp SET DeletedAt=@d, UpdatedAt=@u, SyncDirty=0 WHERE SyncId=@s", con))
                {
                    u.Parameters.AddWithValue("@d", r.Value<string>("deleted_at") ?? updated);
                    u.Parameters.AddWithValue("@u", updated);
                    u.Parameters.AddWithValue("@s", syncId);
                    u.ExecuteNonQuery();
                }
                return true;
            }
            string dealerSync = r.Value<string>("dealer_sync_id") ?? "";
            object did = LocalIdBySync(con, "AddDealer", "Did", dealerSync);
            if (!string.IsNullOrWhiteSpace(dealerSync) && (did == null || did == DBNull.Value))
            {
                StageRemote(con, "zaib_dealer_balance_ops", r);
                advanceCheckpoint = false;
                return false;
            }
            using (var chk = new SQLiteCommand("SELECT SyncId FROM SyncDealerBalanceOp WHERE SyncId=@s", con))
            {
                chk.Parameters.AddWithValue("@s", syncId);
                var exists = chk.ExecuteScalar();
                string sql = exists == null
                    ? @"INSERT INTO SyncDealerBalanceOp(SyncId,DealerSyncId,DdDelta,DDelta,SourceKind,SourceSyncId,DateText,Note,UpdatedAt,SyncDirty,DeletedAt)
                        VALUES(@sid,@ds,@dd,@d,@sk,@src,@dt,@note,@u,0,NULL)"
                    : @"UPDATE SyncDealerBalanceOp SET DealerSyncId=@ds, DdDelta=@dd, DDelta=@d, SourceKind=@sk, SourceSyncId=@src,
                        DateText=@dt, Note=@note, UpdatedAt=@u, SyncDirty=0, DeletedAt=NULL WHERE SyncId=@sid";
                using (var cmd = new SQLiteCommand(sql, con))
                {
                    cmd.Parameters.AddWithValue("@sid", syncId);
                    cmd.Parameters.AddWithValue("@ds", dealerSync);
                    cmd.Parameters.AddWithValue("@dd", r.Value<double?>("dd_delta") ?? 0);
                    cmd.Parameters.AddWithValue("@d", r.Value<double?>("d_delta") ?? 0);
                    cmd.Parameters.AddWithValue("@sk", r.Value<string>("source_kind") ?? "manual");
                    cmd.Parameters.AddWithValue("@src", sourceSyncId);
                    cmd.Parameters.AddWithValue("@dt", r.Value<string>("date_text") ?? "");
                    cmd.Parameters.AddWithValue("@note", r.Value<string>("note") ?? "");
                    cmd.Parameters.AddWithValue("@u", updated);
                    cmd.ExecuteNonQuery();
                }
            }
            ReconcileDealerBalance(con, sourceSyncId, did, r.Value<double?>("dd_delta") ?? 0, r.Value<double?>("d_delta") ?? 0);
            PersistObservedServerRev(con, "SyncDealerBalanceOp", syncId, r);
            return true;
        }

        private static void BackfillBalanceMarkers(SQLiteConnection con)
        {
            if (!TableExists(con, "SyncBalanceApplied")) return;
            string now = DateTime.UtcNow.ToString("o");
            using (var cmd = new SQLiteCommand(
                @"INSERT OR IGNORE INTO SyncBalanceApplied(SourceSyncId, DealerId, DealerSyncId, DdDelta, DDelta, AppliedAt)
                  SELECT p.SyncId, p.Did, d.SyncId, 0, IFNULL(p.AmounGiven,0), @now
                  FROM DieselLedgerCredit p
                  LEFT JOIN AddDealer d ON d.Did = p.Did
                  WHERE p.SyncId IS NOT NULL AND trim(p.SyncId) <> ''", con))
            {
                cmd.Parameters.AddWithValue("@now", now);
                cmd.ExecuteNonQuery();
            }
            using (var cmd = new SQLiteCommand(
                @"INSERT OR IGNORE INTO SyncBalanceApplied(SourceSyncId, DealerId, DealerSyncId, DdDelta, DDelta, AppliedAt)
                  SELECT p.SyncId, p.Did, d.SyncId, IFNULL(p.AmounGiven,0), 0, @now
                  FROM DieselLedgerDebit p
                  LEFT JOIN AddDealer d ON d.Did = p.Did
                  WHERE p.SyncId IS NOT NULL AND trim(p.SyncId) <> ''", con))
            {
                cmd.Parameters.AddWithValue("@now", now);
                cmd.ExecuteNonQuery();
            }
            using (var cmd = new SQLiteCommand(
                @"INSERT OR IGNORE INTO SyncBalanceApplied(SourceSyncId, DealerId, DealerSyncId, DdDelta, DDelta, AppliedAt)
                  SELECT a.SyncId, a.DealerId, d.SyncId, IFNULL(a.AddDisel,0) * IFNULL(a.Rate,0), 0, @now
                  FROM AddStock a
                  LEFT JOIN AddDealer d ON d.Did = a.DealerId
                  WHERE a.SyncId IS NOT NULL AND trim(a.SyncId) <> ''", con))
            {
                cmd.Parameters.AddWithValue("@now", now);
                cmd.ExecuteNonQuery();
            }
        }

        private static void EnsureChildBalanceMarker(SQLiteConnection con, string sourceSyncId, object dealerId, double ddDelta, double dDelta)
        {
            if (string.IsNullOrWhiteSpace(sourceSyncId)) return;
            string dealerSync = DealerSyncId(con, dealerId);
            int? did = null;
            if (dealerId != null && dealerId != DBNull.Value)
                did = Convert.ToInt32(dealerId);
            string now = DateTime.UtcNow.ToString("o");
            using (var cmd = new SQLiteCommand(
                @"INSERT OR IGNORE INTO SyncBalanceApplied(SourceSyncId, DealerId, DealerSyncId, DdDelta, DDelta, AppliedAt)
                  VALUES(@src,@did,@ds,@dd,@d,@a)", con))
            {
                cmd.Parameters.AddWithValue("@src", sourceSyncId);
                cmd.Parameters.AddWithValue("@did", (object)did ?? DBNull.Value);
                cmd.Parameters.AddWithValue("@ds", (object)dealerSync ?? DBNull.Value);
                cmd.Parameters.AddWithValue("@dd", ddDelta);
                cmd.Parameters.AddWithValue("@d", dDelta);
                cmd.Parameters.AddWithValue("@a", now);
                cmd.ExecuteNonQuery();
            }
        }

        private static string DealerSyncId(SQLiteConnection con, object dealerId)
        {
            if (dealerId == null || dealerId == DBNull.Value) return null;
            return GetSyncId(con, "AddDealer", "Did", dealerId);
        }

        private static void ReconcileDealerBalance(SQLiteConnection con, string sourceSyncId, object dealerId, double ddDelta, double dDelta, bool markDealerDirty = false)
        {
            sourceSyncId = NormalizeSyncId(sourceSyncId);
            if (string.IsNullOrWhiteSpace(sourceSyncId)) return;
            int? did = null;
            if (dealerId != null && dealerId != DBNull.Value)
                did = Convert.ToInt32(dealerId);
            string dealerSync = did.HasValue ? DealerSyncId(con, did.Value) : null;

            double prevDd = 0, prevD = 0;
            int? prevDid = null;
            using (var cmd = new SQLiteCommand(
                "SELECT DealerId, DdDelta, DDelta FROM SyncBalanceApplied WHERE SourceSyncId=@s LIMIT 1", con))
            {
                cmd.Parameters.AddWithValue("@s", sourceSyncId);
                using (var rd = cmd.ExecuteReader())
                {
                    if (rd.Read())
                    {
                        if (rd["DealerId"] != DBNull.Value) prevDid = Convert.ToInt32(rd["DealerId"]);
                        prevDd = ToD(rd["DdDelta"]);
                        prevD = ToD(rd["DDelta"]);
                    }
                }
            }

            if (prevDid.HasValue && prevDid == did &&
                Math.Abs(prevDd - ddDelta) < 1e-7 && Math.Abs(prevD - dDelta) < 1e-7)
                return;

            if (prevDid.HasValue && prevDid.Value > 0)
                AdjustDealerBalance(con, prevDid.Value, -prevDd, -prevD, markDealerDirty);

            if (did.HasValue && did.Value > 0 && (Math.Abs(ddDelta) > 1e-9 || Math.Abs(dDelta) > 1e-9))
                AdjustDealerBalance(con, did.Value, ddDelta, dDelta, markDealerDirty);

            if (!did.HasValue || did.Value <= 0 || (Math.Abs(ddDelta) < 1e-9 && Math.Abs(dDelta) < 1e-9))
            {
                using (var cmd = new SQLiteCommand("DELETE FROM SyncBalanceApplied WHERE SourceSyncId=@s", con))
                {
                    cmd.Parameters.AddWithValue("@s", sourceSyncId);
                    cmd.ExecuteNonQuery();
                }
                return;
            }

            string now = DateTime.UtcNow.ToString("o");
            using (var cmd = new SQLiteCommand(
                @"INSERT OR REPLACE INTO SyncBalanceApplied(SourceSyncId, DealerId, DealerSyncId, DdDelta, DDelta, AppliedAt)
                  VALUES(@src,@did,@ds,@dd,@d,@a)", con))
            {
                cmd.Parameters.AddWithValue("@src", sourceSyncId);
                cmd.Parameters.AddWithValue("@did", did.Value);
                cmd.Parameters.AddWithValue("@ds", (object)dealerSync ?? DBNull.Value);
                cmd.Parameters.AddWithValue("@dd", ddDelta);
                cmd.Parameters.AddWithValue("@d", dDelta);
                cmd.Parameters.AddWithValue("@a", now);
                cmd.ExecuteNonQuery();
            }
        }

        private static void ReverseDealerBalance(SQLiteConnection con, string sourceSyncId, bool markDealerDirty = false)
        {
            if (string.IsNullOrWhiteSpace(sourceSyncId)) return;
            double prevDd = 0, prevD = 0;
            int? prevDid = null;
            using (var cmd = new SQLiteCommand(
                "SELECT DealerId, DdDelta, DDelta FROM SyncBalanceApplied WHERE SourceSyncId=@s LIMIT 1", con))
            {
                cmd.Parameters.AddWithValue("@s", sourceSyncId);
                using (var rd = cmd.ExecuteReader())
                {
                    if (!rd.Read()) return;
                    if (rd["DealerId"] != DBNull.Value) prevDid = Convert.ToInt32(rd["DealerId"]);
                    prevDd = ToD(rd["DdDelta"]);
                    prevD = ToD(rd["DDelta"]);
                }
            }
            if (prevDid.HasValue && prevDid.Value > 0)
                AdjustDealerBalance(con, prevDid.Value, -prevDd, -prevD, markDealerDirty);
            using (var cmd = new SQLiteCommand("DELETE FROM SyncBalanceApplied WHERE SourceSyncId=@s", con))
            {
                cmd.Parameters.AddWithValue("@s", sourceSyncId);
                cmd.ExecuteNonQuery();
            }
        }

        private static void AdjustDealerBalance(SQLiteConnection con, int dealerId, double ddDelta, double dDelta, bool markDealerDirty)
        {
            if (dealerId <= 0) return;
            if (Math.Abs(ddDelta) < 1e-9 && Math.Abs(dDelta) < 1e-9) return;
            string now = DateTime.UtcNow.ToString("o");
            string sql = markDealerDirty
                ? "UPDATE AddDealer SET DDAmount = IFNULL(DDAmount,0) + @dd, DAmount = IFNULL(DAmount,0) + @d, UpdatedAt = @u, SyncDirty = 1 WHERE Did = @id"
                : "UPDATE AddDealer SET DDAmount = IFNULL(DDAmount,0) + @dd, DAmount = IFNULL(DAmount,0) + @d, UpdatedAt = @u WHERE Did = @id";
            using (var cmd = new SQLiteCommand(sql, con))
            {
                cmd.Parameters.AddWithValue("@dd", ddDelta);
                cmd.Parameters.AddWithValue("@d", dDelta);
                cmd.Parameters.AddWithValue("@u", now);
                cmd.Parameters.AddWithValue("@id", dealerId);
                if (cmd.ExecuteNonQuery() <= 0)
                    throw new InvalidOperationException("Dealer balance update fail.");
            }
        }

        private static bool UpsertChild(
            SQLiteConnection con,
            string localTable,
            string pk,
            string cloudTable,
            JToken r,
            Action<SQLiteCommand, JToken, object> bind,
            string insertCols,
            string parentTable,
            string parentPk,
            string parentSyncField)
        {
            string syncId = r.Value<string>("sync_id");
            if (string.IsNullOrWhiteSpace(syncId)) return true;
            string updated = r.Value<string>("updated_at") ?? DateTime.UtcNow.ToString("o");
            bool deleted = r["deleted_at"] != null && r["deleted_at"].Type != JTokenType.Null;
            var gate = ClassifyRemoteApply(con, localTable, syncId, updated, deleted, r.Value<string>("device_id"), r.Value<long?>("server_rev"));
            if (gate == RemoteApplyGate.SkipStage)
            {
                if (r is JObject jo) StageRemote(con, cloudTable, jo);
                return false;
            }
            if (gate == RemoteApplyGate.SkipDone)
                return true;
            if (deleted)
            {
                ReverseDealerBalance(con, syncId);
                ApplyRemoteDelete(con, localTable, syncId);
                return true;
            }
            string parentSync = r.Value<string>(parentSyncField);
            object parentId = LocalIdBySync(con, parentTable, parentPk, parentSync);
            if (!string.IsNullOrWhiteSpace(parentSync) && (parentId == null || parentId == DBNull.Value))
            {
                if (r is JObject jo) StageRemote(con, cloudTable, jo);
                return false;
            }
            using (var chk = new SQLiteCommand($"SELECT {pk} FROM {localTable} WHERE {WhereSyncIdEq()} LIMIT 1", con))
            {
                BindNormSync(chk, syncId);
                var exists = chk.ExecuteScalar();
                string sql = exists == null
                    ? $"INSERT INTO {localTable}({insertCols},SyncId,UpdatedAt,SyncDirty) VALUES(@Did,@Date,@AmounGiven,@Note,@s,@u,0)"
                    : $"UPDATE {localTable} SET Did=@Did,Date=@Date,AmounGiven=@AmounGiven,Note=@Note,UpdatedAt=@u,SyncDirty=0 WHERE {WhereSyncIdEq()}";
                using (var cmd = new SQLiteCommand(sql, con))
                {
                    bind(cmd, r, parentId);
                    cmd.Parameters.AddWithValue("@s", syncId);
                    cmd.Parameters.AddWithValue("@u", updated);
                    if (exists != null)
                        BindNormSync(cmd, syncId);
                    RequireApplyRows(cmd.ExecuteNonQuery(), exists == null ? "insert:" + localTable : "update:" + localTable, syncId);
                }
            }
            if (localTable == "DieselLedgerCredit")
                ReconcileDealerBalance(con, syncId, parentId, 0, r.Value<double?>("amount_given") ?? 0);
            else if (localTable == "DieselLedgerDebit")
                ReconcileDealerBalance(con, syncId, parentId, r.Value<double?>("amount_given") ?? 0, 0);
            if (r is JObject joRev) PersistObservedServerRev(con, localTable, syncId, joRev);
            return true;
        }
    }
}

