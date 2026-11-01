using System;
using System.Data.SQLite;

namespace ZaibPetroleumService.Services
{
    public static class DatabaseSchemaManager
    {
        public static void EnsureProfessionalSchema(SQLiteConnection connection)
        {
            EnsureTable(connection, "AuditLog", @"
                CREATE TABLE IF NOT EXISTS AuditLog (
                    Id INTEGER PRIMARY KEY AUTOINCREMENT,
                    ActionTime TEXT NOT NULL,
                    UserName TEXT,
                    UserRole TEXT,
                    ActionType TEXT,
                    TableName TEXT,
                    RecordId TEXT,
                    QuerySummary TEXT,
                    Details TEXT
                );");

            EnsureTable(connection, "RolePermissions", @"
                CREATE TABLE IF NOT EXISTS RolePermissions (
                    Id INTEGER PRIMARY KEY AUTOINCREMENT,
                    RoleName TEXT NOT NULL,
                    FormKey TEXT NOT NULL,
                    CanAccess INTEGER NOT NULL DEFAULT 1,
                    UNIQUE(RoleName, FormKey)
                );");

            EnsureColumn(connection, "BankTransactions", "BankName", "TEXT");
            EnsureColumn(connection, "tblUser", "uRole", "TEXT DEFAULT 'Admin'");
            // Stock (StockDiesel) category — Type / Type2 / Type3 (VIP only; sync payload untouched)
            EnsureColumn(connection, "StockDiesel", "StockType", "TEXT");
            EnsureColumn(connection, "StockDiesel", "StockType2", "TEXT");
            EnsureColumn(connection, "StockDiesel", "StockType3", "TEXT");

            // Daily Diesel Sales VIP — Location / Item / Vehicle Model / InOut (local only; sync payload untouched)
            EnsureColumn(connection, "PetrolAdd", "SaleLocation", "TEXT");
            EnsureColumn(connection, "PetrolAdd", "SaleItem", "TEXT");
            EnsureColumn(connection, "PetrolAdd", "SaleVehicleModel", "TEXT");
            EnsureColumn(connection, "PetrolAdd", "SaleInOut", "TEXT");

            // DealerAmount (AddStock) VIP — Credit / Amount / Location / Item / Vehicle Model / InOut (local; sync untouched)
            EnsureColumn(connection, "AddStock", "Credit", "REAL DEFAULT 0");
            EnsureColumn(connection, "AddStock", "Amount", "REAL DEFAULT 0");
            EnsureColumn(connection, "AddStock", "SaleLocation", "TEXT");
            EnsureColumn(connection, "AddStock", "SaleItem", "TEXT");
            EnsureColumn(connection, "AddStock", "SaleVehicleModel", "TEXT");
            EnsureColumn(connection, "AddStock", "SaleInOut", "TEXT");
            // Purani entries → In (sirf blank/NULL; Amount/Credit/DDAmount touch nahi)
            try
            {
                using (var cmd = connection.CreateCommand())
                {
                    cmd.CommandText =
                        "UPDATE AddStock SET SaleInOut='In' WHERE SaleInOut IS NULL OR TRIM(IFNULL(SaleInOut,''))=''";
                    cmd.ExecuteNonQuery();
                }
            }
            catch { }

            // Stock names — AddDealer se bilkul alag (SDid → StockPartyName.Pid)
            EnsureTable(connection, "StockPartyName", @"
                CREATE TABLE IF NOT EXISTS StockPartyName (
                    Pid INTEGER PRIMARY KEY AUTOINCREMENT,
                    PartyName TEXT NOT NULL,
                    Date TEXT
                );");
            EnsureDefaultStockParty(connection, "RozNamcha");

            // Bul Mal — local only (sync whitelist mein NAHI)
            EnsureTable(connection, "BulMalOwner", @"
                CREATE TABLE IF NOT EXISTS BulMalOwner (
                    id INTEGER PRIMARY KEY AUTOINCREMENT,
                    Name TEXT NOT NULL,
                    Date TEXT
                );");
            EnsureTable(connection, "BulMalEntry", @"
                CREATE TABLE IF NOT EXISTS BulMalEntry (
                    id INTEGER PRIMARY KEY AUTOINCREMENT,
                    OwnerId INTEGER NOT NULL,
                    Date TEXT,
                    Type TEXT,
                    Amount REAL DEFAULT 0,
                    Note TEXT
                );");
            EnsureBulMalPermission(connection);
            InsertPerm(connection, "Admin", "frmDealerAmountCombinedView", 1);
            InsertPerm(connection, "Manager", "frmDealerAmountCombinedView", 1);

            // Sync columns for Supabase zaibservice (Mobile ↔ PC)
            string[] syncTables =
            {
                "AddCustomer","PetrolAdd","AddDealer","DieselLedgerCredit","AddStock",
                "DieselLedgerDebit","StockDiesel","BankTransactions","Expensetable"
            };
            foreach (string t in syncTables)
            {
                EnsureColumn(connection, t, "SyncId", "TEXT");
                EnsureColumn(connection, t, "UpdatedAt", "TEXT");
                EnsureColumn(connection, t, "SyncDirty", "INTEGER DEFAULT 1");
            }

            SeedDefaultPermissions(connection);
        }

        private static void EnsureTable(SQLiteConnection connection, string tableName, string createSql)
        {
            using (SQLiteCommand check = new SQLiteCommand(
                "SELECT name FROM sqlite_master WHERE type='table' AND name=@name", connection))
            {
                check.Parameters.AddWithValue("@name", tableName);
                object found = check.ExecuteScalar();
                if (found != null && found != DBNull.Value)
                    return;
            }

            using (SQLiteCommand create = new SQLiteCommand(createSql, connection))
                create.ExecuteNonQuery();
        }

        public static void EnsureColumn(SQLiteConnection connection, string tableName, string columnName, string columnType)
        {
            if (!TableExists(connection, tableName))
                return;

            using (SQLiteCommand cmd = new SQLiteCommand($"PRAGMA table_info({tableName})", connection))
            using (SQLiteDataReader reader = cmd.ExecuteReader())
            {
                while (reader.Read())
                {
                    if (string.Equals(reader["name"].ToString(), columnName, StringComparison.OrdinalIgnoreCase))
                        return;
                }
            }

            using (SQLiteCommand alter = new SQLiteCommand(
                $"ALTER TABLE {tableName} ADD COLUMN {columnName} {columnType}", connection))
            {
                alter.ExecuteNonQuery();
            }
        }

        private static bool TableExists(SQLiteConnection connection, string tableName)
        {
            using (SQLiteCommand cmd = new SQLiteCommand(
                "SELECT 1 FROM sqlite_master WHERE type='table' AND name=@name LIMIT 1", connection))
            {
                cmd.Parameters.AddWithValue("@name", tableName);
                return cmd.ExecuteScalar() != null;
            }
        }

        private static void SeedDefaultPermissions(SQLiteConnection connection)
        {
            using (SQLiteCommand countCmd = new SQLiteCommand("SELECT COUNT(*) FROM RolePermissions", connection))
            {
                if (Convert.ToInt32(countCmd.ExecuteScalar()) > 0)
                    return;
            }

            string[] allForms =
            {
                "frmDashBoard","frmCustomerView","frmDiselView","frmCreditAdjust",
                "frmCustomerToCustomerView","frmDieselLedger","frmDealerNameView",
                "frmStockView","frmDealertoDealerView","frmDieselLedgerView",
                "frmDealerAmountCombinedView",
                "FrmDirectDealerPaymentAmountView","frmBankAccountView","frmExpense",
                "frmBulMalView","frmStockDieselView","frmClosingformEntry","frmClosing2",
                "ReportAndBackup","changepassword","ProfitLossReportForm",
                "BalanceSheetReportForm","AuditTrailView","RoleAccessView"
            };

            string[] managerSkip = { "RoleAccessView", "changepassword" };
            string[] cashierAllow =
            {
                "frmDashBoard","frmCustomerView","frmDiselView","frmCreditAdjust","ReportAndBackup"
            };

            foreach (string form in allForms)
            {
                InsertPerm(connection, "Admin", form, 1);
                InsertPerm(connection, "Manager", form, Array.IndexOf(managerSkip, form) >= 0 ? 0 : 1);
                InsertPerm(connection, "Cashier", form, Array.IndexOf(cashierAllow, form) >= 0 ? 1 : 0);
            }
        }

        private static void EnsureBulMalPermission(SQLiteConnection connection)
        {
            // Existing DB pe bhi Admin/Manager ko Bul Mal mil jaye (sync nahi)
            InsertPerm(connection, "Admin", "frmBulMalView", 1);
            InsertPerm(connection, "Manager", "frmBulMalView", 1);
        }

        private static void EnsureDefaultStockParty(SQLiteConnection connection, string partyName)
        {
            if (string.IsNullOrWhiteSpace(partyName)) return;
            using (SQLiteCommand find = new SQLiteCommand(
                "SELECT Pid FROM StockPartyName WHERE TRIM(IFNULL(PartyName,'')) = @n COLLATE NOCASE LIMIT 1",
                connection))
            {
                find.Parameters.AddWithValue("@n", partyName.Trim());
                object found = find.ExecuteScalar();
                if (found != null && found != DBNull.Value)
                    return;
            }

            using (SQLiteCommand ins = new SQLiteCommand(
                "INSERT INTO StockPartyName (PartyName, Date) VALUES (@name, @date)", connection))
            {
                ins.Parameters.AddWithValue("@name", partyName.Trim());
                ins.Parameters.AddWithValue("@date", DateTime.Now.ToString("yyyy-MM-dd"));
                ins.ExecuteNonQuery();
            }
        }

        private static void InsertPerm(SQLiteConnection connection, string role, string formKey, int canAccess)
        {
            using (SQLiteCommand cmd = new SQLiteCommand(
                "INSERT OR IGNORE INTO RolePermissions (RoleName, FormKey, CanAccess) VALUES (@role, @form, @access)", connection))
            {
                cmd.Parameters.AddWithValue("@role", role);
                cmd.Parameters.AddWithValue("@form", formKey);
                cmd.Parameters.AddWithValue("@access", canAccess);
                cmd.ExecuteNonQuery();
            }
        }
    }
}
