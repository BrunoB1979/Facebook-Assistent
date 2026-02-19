using Facebook_Assistent.Models;
using System;
using System.Data.SQLite;
using System.IO;
using System.Collections.Generic;

namespace Facebook_Assistent.Services
{
    public static class DatabaseHelper
    {
        // Pfade definieren
        private static readonly string BaseDir = AppDomain.CurrentDomain.BaseDirectory;
        private static readonly string DbFolder = Path.Combine(BaseDir, "Datenbank");
        private static readonly string ImagesFolder = Path.Combine(BaseDir, "Bilder");
        public static readonly string DbPath = Path.Combine(DbFolder, "database.sqlite");

        // Connection String für SQLite
        private static string ConnectionString => $"Data Source={DbPath};Version=3;";

        /// <summary>
        /// Erstellt Ordner und Tabellen, falls sie nicht existieren.
        /// </summary>
        public static void InitializeDatabase()
        {
            // 1. Ordner erstellen
            if (!Directory.Exists(DbFolder)) Directory.CreateDirectory(DbFolder);
            if (!Directory.Exists(ImagesFolder)) Directory.CreateDirectory(ImagesFolder);

            // 2. Datenbankdatei und Tabellen erstellen
            if (!File.Exists(DbPath))
            {
                SQLiteConnection.CreateFile(DbPath);
            }

            using (var conn = new SQLiteConnection(ConnectionString))
            {
                conn.Open();

                // Tabelle Settings
                string sqlSettings = @"
                    CREATE TABLE IF NOT EXISTS Settings (
                        AppId TEXT,
                        PageId TEXT,
                        AccessToken TEXT
                    );";
                ExecuteCommand(sqlSettings, conn);

                // Tabelle Posts
                string sqlPosts = @"
                    CREATE TABLE IF NOT EXISTS Posts (
                        Id INTEGER PRIMARY KEY AUTOINCREMENT,
                        Headline TEXT,
                        FullText TEXT,
                        ImagePath TEXT,
                        Status INTEGER DEFAULT 0,
                        FacebookPostId TEXT,
                        PublishedDate TEXT,
                        LikesCount INTEGER DEFAULT 0,
                        CommentsCount INTEGER DEFAULT 0
                    );";
                ExecuteCommand(sqlPosts, conn);
            }
        }

        public static void DeletePost(int id)
        {
            using (var conn = GetConnection())
            {
                conn.Open();
                string sql = "DELETE FROM Posts WHERE Id = @id";

                using (var cmd = new SQLiteCommand(sql, conn))
                {
                    cmd.Parameters.AddWithValue("@id", id);
                    cmd.ExecuteNonQuery();
                }
            }
        }

        public static Post GetPostById(int id)
        {
            using (var conn = GetConnection())
            {
                conn.Open();
                string sql = "SELECT * FROM Posts WHERE Id = @id";
                using (var cmd = new SQLiteCommand(sql, conn))
                {
                    cmd.Parameters.AddWithValue("@id", id);
                    using (var reader = cmd.ExecuteReader())
                    {
                        if (reader.Read())
                        {
                            // Wir lesen nur den ImagePath, der Rest ist für das Löschen egal
                            return new Post
                            {
                                Id = Convert.ToInt32(reader["Id"]),
                                ImagePath = reader["ImagePath"].ToString(),
                                // Die anderen Felder füllen wir hier faulheitshalber nicht, 
                                // da wir nur das Bild wissen wollen.
                            };
                        }
                    }
                }
            }
            return null;
        }


        public static void SavePost(Post post)
        {
            using (var conn = GetConnection())
            {
                conn.Open();
                string sql = @"
            INSERT INTO Posts (Headline, FullText, ImagePath, Status, PublishedDate, LikesCount, CommentsCount)
            VALUES (@headline, @fullText, @imagePath, @status, @publishedDate, 0, 0)";

                using (var cmd = new SQLiteCommand(sql, conn))
                {
                    // Parameter verhindern SQL-Injection
                    cmd.Parameters.AddWithValue("@headline", post.Headline);
                    cmd.Parameters.AddWithValue("@fullText", post.FullText);
                    cmd.Parameters.AddWithValue("@imagePath", post.ImagePath);
                    cmd.Parameters.AddWithValue("@status", post.Status);
                    cmd.Parameters.AddWithValue("@publishedDate", post.PublishedDate);

                    cmd.ExecuteNonQuery();
                }
            }
        }

        /// <summary>
        /// Lädt die Einstellungen aus der Datenbank. Gibt null zurück, wenn noch nichts gespeichert wurde.
        /// </summary>

        public static List<Post> GetPosts()
        {
            var list = new List<Post>();

            using (var conn = GetConnection())
            {
                conn.Open();
                string sql = "SELECT * FROM Posts ORDER BY Id DESC"; // Neueste zuerst

                using (var cmd = new SQLiteCommand(sql, conn))
                using (var reader = cmd.ExecuteReader())
                {
                    while (reader.Read())
                    {
                        list.Add(new Post
                        {
                            Id = Convert.ToInt32(reader["Id"]),
                            Headline = reader["Headline"].ToString(),
                            FullText = reader["FullText"].ToString(),
                            ImagePath = reader["ImagePath"].ToString(),
                            Status = Convert.ToInt32(reader["Status"]),
                            // Bei DateTime und Nullable-Typen muss man vorsichtig sein
                            FacebookPostId = reader["FacebookPostId"] as string,
                            LikesCount = Convert.ToInt32(reader["LikesCount"]),
                            CommentsCount = Convert.ToInt32(reader["CommentsCount"]),
                            // Datum sicher parsen (falls null, dann null)
                            PublishedDate = reader["PublishedDate"] == DBNull.Value ? (DateTime?)null : DateTime.Parse(reader["PublishedDate"].ToString())
                        });
                    }
                }
            }
            return list;
        }

        public static void UpdatePost(Post post)
        {
            using (var conn = GetConnection())
            {
                conn.Open();
                string sql = @"
            UPDATE Posts 
            SET Headline = @headline, 
                FullText = @fullText, 
                ImagePath = @imagePath
            WHERE Id = @id";

                using (var cmd = new SQLiteCommand(sql, conn))
                {
                    cmd.Parameters.AddWithValue("@headline", post.Headline);
                    cmd.Parameters.AddWithValue("@fullText", post.FullText);
                    cmd.Parameters.AddWithValue("@imagePath", post.ImagePath);
                    cmd.Parameters.AddWithValue("@id", post.Id); // Wichtig: Welche Zeile?

                    cmd.ExecuteNonQuery();
                }
            }
        }


        public static FacebookSettings LoadSettings()
        {
            using (var conn = GetConnection())
            {
                conn.Open();
                string sql = "SELECT AppId, PageId, AccessToken FROM Settings LIMIT 1";

                using (var cmd = new SQLiteCommand(sql, conn))
                using (var reader = cmd.ExecuteReader())
                {
                    if (reader.Read())
                    {
                        return new FacebookSettings
                        {
                            AppId = reader["AppId"].ToString(),
                            PageId = reader["PageId"].ToString(),
                            AccessToken = reader["AccessToken"].ToString()
                        };
                    }
                }
            }
            return null; // Keine Einstellungen gefunden
        }

 
        public static void SaveSettings(FacebookSettings settings)
        /// Speichert die Einstellungen. Überschreibt vorhandene Daten (Löschen -> Neu einfügen).

        {
            using (var conn = GetConnection())
            {
                conn.Open();

                // Transaktion starten, damit Löschen+Einfügen sicher zusammen passiert
                using (var transaction = conn.BeginTransaction())
                {
                    // 1. Alte Einstellungen löschen (damit immer nur 1 Zeile existiert)
                    using (var cmdDelete = new SQLiteCommand("DELETE FROM Settings", conn))
                    {
                        cmdDelete.ExecuteNonQuery();
                    }

                    // 2. Neue Einstellungen einfügen
                    string sqlInsert = "INSERT INTO Settings (AppId, PageId, AccessToken) VALUES (@appId, @pageId, @token)";
                    using (var cmdInsert = new SQLiteCommand(sqlInsert, conn))
                    {
                        cmdInsert.Parameters.AddWithValue("@appId", settings.AppId);
                        cmdInsert.Parameters.AddWithValue("@pageId", settings.PageId);
                        cmdInsert.Parameters.AddWithValue("@token", settings.AccessToken);
                        cmdInsert.ExecuteNonQuery();
                    }

                    transaction.Commit();
                }
            }
        }

        private static void ExecuteCommand(string sql, SQLiteConnection conn)
        {
            using (var cmd = new SQLiteCommand(sql, conn))
            {
                cmd.ExecuteNonQuery();
            }
        }

        // Hilfsmethode, um später einfach an die Verbindung zu kommen
        public static SQLiteConnection GetConnection()
        {
            return new SQLiteConnection(ConnectionString);
        }

        public static void MarkAsPublished(int localId, string facebookPostId)
        {
            using (var conn = GetConnection())
            {
                conn.Open();
                string sql = @"
                    UPDATE Posts 
                    SET Status = 1, 
                        FacebookPostId = @fbId, 
                        PublishedDate = @date 
                    WHERE Id = @localId";

                using (var cmd = new SQLiteCommand(sql, conn))
                {
                    cmd.Parameters.AddWithValue("@fbId", facebookPostId);
                    // Aktuelles Datum als String speichern (ISO Format ist gut für SQLite)
                    cmd.Parameters.AddWithValue("@date", DateTime.Now.ToString("yyyy-MM-dd HH:mm:ss"));
                    cmd.Parameters.AddWithValue("@localId", localId);

                    cmd.ExecuteNonQuery();
                }
            }
        }

        public static void UpdatePostStats(int localId, int likes, int comments)
        {
            using (var conn = GetConnection())
            {
                conn.Open();
                string sql = "UPDATE Posts SET LikesCount = @likes, CommentsCount = @comments WHERE Id = @id";

                using (var cmd = new SQLiteCommand(sql, conn))
                {
                    cmd.Parameters.AddWithValue("@likes", likes);
                    cmd.Parameters.AddWithValue("@comments", comments);
                    cmd.Parameters.AddWithValue("@id", localId);
                    cmd.ExecuteNonQuery();
                }
            }
        }
    }


}
