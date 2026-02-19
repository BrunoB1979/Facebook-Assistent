using Newtonsoft.Json.Linq; // Zum Lesen der Antwort
using System;
using System.IO;
using System.Net.Http; // Für die Web-Anfrage
using System.Threading.Tasks; // Für asynchrone Tasks (damit die App nicht einfriert)

namespace Facebook_Assistent.Services
{
    public static class FacebookApiService
    {
        private static readonly HttpClient client = new HttpClient();

        /// <summary>
        /// Testet die Verbindung zu Facebook.
        /// Gibt bei Erfolg den Namen der Seite zurück.
        /// Wirft eine Exception bei Fehler.
        /// </summary>

        public static async Task<string> PublishPhotoPost(string pageId, string accessToken, string message, string imagePath)
        {
            // Der Endpunkt für Fotos: https://graph.facebook.com/{page-id}/photos
            string url = $"https://graph.facebook.com/{pageId}/photos";

            try
            {
                // Wir bauen ein "Formular" im Speicher, das die Datei und die Texte enthält
                using (var formData = new MultipartFormDataContent())
                {
                    // 1. Der Text (Caption)
                    formData.Add(new StringContent(message), "message");

                    // 2. Der Access Token
                    formData.Add(new StringContent(accessToken), "access_token");

                    // 3. Das Bild selbst (als Byte-Stream)
                    // Wir öffnen die Datei, lesen sie und packen sie in das Formular
                    byte[] imageBytes = File.ReadAllBytes(imagePath);
                    var imageContent = new ByteArrayContent(imageBytes);

                    // Dateiname ist für FB nicht so wichtig, muss aber da sein
                    formData.Add(imageContent, "source", "upload.jpg");

                    // 4. Absenden!
                    HttpResponseMessage response = await client.PostAsync(url, formData);
                    string jsonResponse = await response.Content.ReadAsStringAsync();

                    if (!response.IsSuccessStatusCode)
                    {
                        var errorJson = JObject.Parse(jsonResponse);
                        string fbError = errorJson["error"]?["message"]?.ToString() ?? "Upload fehlgeschlagen";
                        throw new Exception(fbError);
                    }

                    // 5. Erfolg: ID zurückgeben
                    var data = JObject.Parse(jsonResponse);

                    // Facebook gibt oft "id" und "post_id" zurück. Wir nehmen die ID.
                    return data["id"].ToString();
                }
            }
            catch (HttpRequestException)
            {
                throw new Exception("Netzwerkfehler beim Upload.");
            }
            catch (IOException)
            {
                throw new Exception("Konnte auf die Bilddatei nicht zugreifen (vielleicht geöffnet?).");
            }
        }


        public static async Task<string> ValidateConnection(string pageId, string accessToken)
        {
            // Die URL für den Graph API Request (Wir fragen nur nach dem Namen der Seite)
            string url = $"https://graph.facebook.com/{pageId}?fields=name&access_token={accessToken}";

            try
            {
                // 1. Anfrage senden
                HttpResponseMessage response = await client.GetAsync(url);

                // 2. Antwort als String lesen
                string jsonResponse = await response.Content.ReadAsStringAsync();

                // 3. Fehler prüfen (z.B. 400 Bad Request, wenn Token falsch)
                if (!response.IsSuccessStatusCode)
                {
                    // Versuchen, die Fehlermeldung von Facebook zu lesen
                    var errorJson = JObject.Parse(jsonResponse);
                    string fbError = errorJson["error"]?["message"]?.ToString() ?? "Unbekannter API Fehler";
                    throw new Exception($"Facebook sagt: {fbError}");
                }

                // 4. Erfolg! Name extrahieren
                var data = JObject.Parse(jsonResponse);
                string pageName = data["name"].ToString();

                return pageName;
            }
            catch (HttpRequestException)
            {
                throw new Exception("Netzwerkfehler. Bitte Internetverbindung prüfen.");
            }
        }

        public static async Task<(int likes, int comments)> GetPostStatistics(string fbPostId, string accessToken)
        {
            // Wir nutzen "summary(true)", um die Gesamtzahl zu bekommen
            string url = $"https://graph.facebook.com/{fbPostId}?fields=likes.summary(true),comments.summary(true)&access_token={accessToken}";

            try
            {
                HttpResponseMessage response = await client.GetAsync(url);
                string jsonResponse = await response.Content.ReadAsStringAsync();

                if (!response.IsSuccessStatusCode)
                {
                    // Falls der Post gelöscht wurde oder Token ungültig ist, geben wir -1 zurück
                    return (-1, -1);
                }

                var data = JObject.Parse(jsonResponse);

                // Sicher navigieren: data["likes"]["summary"]["total_count"]
                int likes = 0;
                if (data["likes"] != null && data["likes"]["summary"] != null)
                {
                    likes = (int)data["likes"]["summary"]["total_count"];
                }

                int comments = 0;
                if (data["comments"] != null && data["comments"]["summary"] != null)
                {
                    comments = (int)data["comments"]["summary"]["total_count"];
                }

                return (likes, comments);
            }
            catch
            {
                return (0, 0); // Bei Fehler einfach 0 annehmen
            }
        }
    }
}
