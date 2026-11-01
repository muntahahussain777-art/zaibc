using System;
using System.Collections;
using System.Data;
using System.Data.SQLite;
using ZaibPetroleumService.ProjectConnection;

namespace ZaibPetroleumService.Services
{
    /// <summary>
    /// Stock-only party names. Never touches AddDealer.
    /// StockDiesel.SDid stores StockPartyName.Pid.
    /// </summary>
    public static class StockPartyNameStore
    {
        public const string DefaultName = "RozNamcha";

        public static void EnsureSchema()
        {
            try
            {
                using (var con = new SQLiteConnection(projectconnection.ConnectionString))
                {
                    con.Open();
                    DatabaseSchemaManager.EnsureProfessionalSchema(con);
                }
            }
            catch { }
        }

        public static DataTable ListAll()
        {
            EnsureSchema();
            return MainClass.ExecuteSelectQuery(
                @"SELECT Pid AS Did, PartyName AS DealerName
                  FROM StockPartyName
                  ORDER BY PartyName COLLATE NOCASE
                  LIMIT 2000", null);
        }

        public static DataTable ListForGrid()
        {
            EnsureSchema();
            return MainClass.ExecuteSelectQuery(
                @"SELECT Pid AS Id, PartyName AS Name
                  FROM StockPartyName
                  ORDER BY PartyName COLLATE NOCASE", null);
        }

        public static int FindId(string name)
        {
            if (string.IsNullOrWhiteSpace(name)) return 0;
            DataTable dt = MainClass.ExecuteSelectQuery(
                "SELECT Pid FROM StockPartyName WHERE TRIM(IFNULL(PartyName,'')) = @n COLLATE NOCASE LIMIT 1",
                new Hashtable { { "@n", name.Trim() } });
            if (dt == null || dt.Rows.Count == 0 || dt.Rows[0]["Pid"] == DBNull.Value)
                return 0;
            return Convert.ToInt32(dt.Rows[0]["Pid"]);
        }

        public static int EnsureExists(string name)
        {
            EnsureSchema();
            string n = (name ?? "").Trim();
            if (string.IsNullOrWhiteSpace(n)) return 0;
            int id = FindId(n);
            if (id > 0) return id;

            int r = MainClass.DataInsertUpdateDelete(
                "INSERT INTO StockPartyName (PartyName, Date) VALUES (@name, @date)",
                new Hashtable
                {
                    { "@name", n },
                    { "@date", DateTime.Now.ToString("yyyy-MM-dd") }
                });
            if (r <= 0) return 0;
            return FindId(n);
        }

        public static int EnsureDefault()
        {
            return EnsureExists(DefaultName);
        }

        public static int Insert(string name)
        {
            return EnsureExists(name);
        }

        public static bool Update(int pid, string name)
        {
            if (pid <= 0 || string.IsNullOrWhiteSpace(name)) return false;
            int r = MainClass.DataInsertUpdateDelete(
                "UPDATE StockPartyName SET PartyName = @name WHERE Pid = @id",
                new Hashtable { { "@name", name.Trim() }, { "@id", pid } });
            return r > 0;
        }

        public static bool Delete(int pid)
        {
            if (pid <= 0) return false;
            int r = MainClass.DataInsertUpdateDelete(
                "DELETE FROM StockPartyName WHERE Pid = @id",
                new Hashtable { { "@id", pid } });
            return r > 0;
        }

        public static int CountStockUsage(int pid)
        {
            if (pid <= 0) return 0;
            DataTable used = MainClass.ExecuteSelectQuery(
                "SELECT COUNT(*) AS C FROM StockDiesel WHERE SDid = @id",
                new Hashtable { { "@id", pid } });
            if (used == null || used.Rows.Count == 0 || used.Rows[0]["C"] == DBNull.Value)
                return 0;
            return Convert.ToInt32(used.Rows[0]["C"]);
        }
    }
}
