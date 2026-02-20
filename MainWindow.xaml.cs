using Facebook_Assistent.Models;
using Facebook_Assistent.Services;
using Microsoft.Win32; // Für OpenFileDialog
using System;
using System.Collections.Generic;
using System.IO;
using System.Globalization;
using System.Linq; // WICHTIG für das Filtern
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media.Imaging;

namespace Facebook_Assistent
{
    public partial class MainWindow : Window
    {
        // --- VARIABLEN ---

        // Speichert den Pfad zum aktuellen Bild (für die Vorschau)
        private string _currentSelectedImagePath = string.Empty;

        // Merkt sich die ID des Posts, der gerade bearbeitet wird.
        // -1 bedeutet: Wir erstellen gerade einen komplett neuen Post.
        private int _editingPostId = -1;

        private class StatsPostRow
        {
            public int Rank { get; set; }
            public string Headline { get; set; }
            public string PublishedDisplay { get; set; }
            public int Likes { get; set; }
            public int Comments { get; set; }
            public int Shares { get; set; }
            public int Interactions { get; set; }
            public int EngagementScore { get; set; }
            public string InteractionsPerDay { get; set; }
            public int DaysOnline { get; set; }
            public string FacebookPostId { get; set; }
        }

        private class CommentRow
        {
            public int Index { get; set; }
            public string Author { get; set; }
            public string CreatedDisplay { get; set; }
            public string Message { get; set; }
        }

        public MainWindow()
        {
            InitializeComponent();
            InitializeApplication();
        }

        private void InitializeApplication()
        {
            try
            {
                DatabaseHelper.InitializeDatabase();

                // Einstellungen laden
                LoadSettingsToUI();

                lblStatus.Text = "Datenbank bereit. Einstellungen geladen.";
            }
            catch (Exception ex)
            {
                MessageBox.Show($"Start-Fehler: {ex.Message}");
            }
        }

        // ==========================================
        // TAB 1: EINSTELLUNGEN
        // ==========================================

        private void BtnSaveSettings_Click(object sender, RoutedEventArgs e)
        {
            try
            {
                var settings = new FacebookSettings
                {
                    AppId = txtAppId.Text,
                    PageId = txtPageId.Text,
                    AccessToken = txtAccessToken.Text
                };

                DatabaseHelper.SaveSettings(settings);
                MessageBox.Show("Einstellungen gespeichert!", "Erfolg", MessageBoxButton.OK, MessageBoxImage.Information);
            }
            catch (Exception ex)
            {
                MessageBox.Show($"Fehler: {ex.Message}");
            }
        }

        // ==========================================
        // TAB 2: POST EDITOR
        // ==========================================

        private void TxtPostContent_TextChanged(object sender, TextChangedEventArgs e)
        {
            UpdatePostPreview();
            ValidateInput();
        }

        private void BtnSelectImage_Click(object sender, RoutedEventArgs e)
        {
            OpenFileDialog openFileDialog = new OpenFileDialog
            {
                Filter = "Bilder|*.jpg;*.jpeg;*.png;*.bmp",
                Title = "Bild auswählen"
            };

            if (openFileDialog.ShowDialog() == true)
            {
                _currentSelectedImagePath = openFileDialog.FileName;
                txtImagePath.Text = _currentSelectedImagePath;
                ShowImageInPreview(_currentSelectedImagePath);
                ValidateInput();
            }
        }

        private void BtnSavePost_Click(object sender, RoutedEventArgs e)
        {
            try
            {
                // A. Bild kopieren (falls es nicht schon im App-Ordner liegt)
                string finalImagePath = _currentSelectedImagePath;

                if (!string.IsNullOrEmpty(finalImagePath) && File.Exists(finalImagePath))
                {
                    // Wenn der Pfad noch NICHT unseren BaseDirectory enthält, müssen wir kopieren
                    if (!finalImagePath.Contains(AppDomain.CurrentDomain.BaseDirectory))
                    {
                        string extension = Path.GetExtension(finalImagePath);
                        string newFileName = $"{Guid.NewGuid()}{extension}";
                        string destinationPath = Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "Bilder", newFileName);

                        File.Copy(finalImagePath, destinationPath, true);
                        finalImagePath = destinationPath;
                    }
                }

                // B. Post-Objekt erstellen
                Post postToSave = new Post
                {
                    Headline = GetFirstLine(txtPostContent.Text),
                    FullText = txtPostContent.Text,
                    ImagePath = finalImagePath,
                    Status = 0, // Entwurf
                    PublishedDate = null,
                    LikesCount = 0,
                    CommentsCount = 0,
                    SharesCount = 0
                };

                // C. Entscheiden: Neu anlegen oder Update?
                if (_editingPostId == -1)
                {
                    // NEU
                    DatabaseHelper.SavePost(postToSave);
                    MessageBox.Show("Beitrag als Entwurf gespeichert.");
                }
                else
                {
                    // UPDATE (Bearbeiten)

                    // 1. Das ALTE Bild ermitteln, bevor wir überschreiben
                    var oldPostData = DatabaseHelper.GetPostById(_editingPostId);

                    // 2. Datenbank aktualisieren
                    postToSave.Id = _editingPostId;
                    DatabaseHelper.UpdatePost(postToSave);

                    // 3. Müllabfuhr: Wenn sich das Bild geändert hat, das alte löschen
                    if (oldPostData != null && oldPostData.ImagePath != postToSave.ImagePath)
                    {
                        try
                        {
                            // Prüfen, ob die Datei existiert und ob sie wirklich in UNSEREM Bilder-Ordner liegt
                            // (Wir wollen ja nicht versehentlich Original-Bilder vom Desktop des Nutzers löschen)
                            if (File.Exists(oldPostData.ImagePath) &&
                                oldPostData.ImagePath.Contains(AppDomain.CurrentDomain.BaseDirectory))
                            {
                                // Da das Bild noch in der Vorschau (Image-Control) geladen sein könnte, 
                                // erzwingen wir, dass der Garbage Collector (Müllabfuhr des RAM) aufräumt,
                                // damit die Datei freigegeben wird.
                                GC.Collect();
                                GC.WaitForPendingFinalizers();

                                File.Delete(oldPostData.ImagePath);
                            }
                        }
                        catch (Exception ex)
                        {
                            // Wenn das Löschen fehlschlägt (z.B. Datei gesperrt), ist das nicht schlimm.
                            // Wir loggen es nur (oder ignorieren es hier), damit die App nicht abstürzt.
                            Console.WriteLine("Konnte altes Bild nicht löschen: " + ex.Message);
                        }
                    }

                    MessageBox.Show("Beitrag wurde aktualisiert (altes Bild bereinigt).");
                }

                // D. Aufräumen
                ClearEditor();
                lblStatus.Text = "Gespeichert.";
            }
            catch (Exception ex)
            {
                MessageBox.Show($"Fehler beim Speichern: {ex.Message}");
            }
        }

        private void BtnClearEditor_Click(object sender, RoutedEventArgs e)
        {
            ClearEditor();
        }

        // ==========================================
        // TAB 3: MANAGEMENT
        // ==========================================

        private void TabControl_SelectionChanged(object sender, SelectionChangedEventArgs e)
        {
            if (e.Source is TabControl)
            {
                if (tabManagement.IsSelected)
                {
                    LoadPostsList();
                }
                else if (tabStats.IsSelected) // Jetzt kennt er "tabStats"
                {
                    UpdateStatisticsUI(); // Lädt die lokalen Daten in die Anzeige
                }
            }
        }

        private void BtnRefreshList_Click(object sender, RoutedEventArgs e)
        {
            LoadPostsList();
        }

        private void CmbFilter_SelectionChanged(object sender, SelectionChangedEventArgs e)
        {
            LoadPostsList();
        }

        private void BtnEditPost_Click(object sender, RoutedEventArgs e)
        {
            if (gridPosts.SelectedItem is Post selectedPost)
            {
                if (selectedPost.Status == 1)
                {
                    MessageBox.Show("Veröffentlichte Beiträge können nicht bearbeitet werden.");
                    return;
                }

                // Modus aktivieren
                _editingPostId = selectedPost.Id;

                // UI füllen
                txtPostContent.Text = selectedPost.FullText;
                _currentSelectedImagePath = selectedPost.ImagePath;
                txtImagePath.Text = _currentSelectedImagePath;

                ShowImageInPreview(_currentSelectedImagePath);

                // Button umbenennen und Tab wechseln
                btnSavePost.Content = "Änderungen speichern";
                tabEditor.IsSelected = true;
            }
            else
            {
                MessageBox.Show("Bitte wähle einen Beitrag aus.");
            }
        }

        // ==========================================
        // HILFSMETHODEN
        // ==========================================

        private void LoadPostsList()
        {
            // Sicherheits-Check gegen Start-Abstürze
            if (cmbFilter == null || gridPosts == null) return;

            try
            {
                var allPosts = DatabaseHelper.GetPosts();
                List<Post> filteredList;

                // Filtern
                if (cmbFilter.SelectedIndex == 1) // Nur Entwürfe
                {
                    filteredList = allPosts.Where(p => p.Status == 0).ToList();
                }
                else if (cmbFilter.SelectedIndex == 2) // Nur Veröffentlichte
                {
                    filteredList = allPosts.Where(p => p.Status == 1).ToList();
                }
                else // Alle
                {
                    filteredList = allPosts;
                }

                gridPosts.ItemsSource = filteredList;
                lblStatus.Text = $"{filteredList.Count} Beiträge geladen.";
            }
            catch (Exception ex)
            {
                MessageBox.Show($"Ladefehler: {ex.Message}");
            }
        }

        private void ClearEditor()
        {
            txtPostContent.Text = "";
            txtImagePath.Text = "Kein Bild ausgewählt.";
            _currentSelectedImagePath = "";
            imgPreview.Source = null;
            imgEditorPreview.Source = null;

            // RESET des Modus
            _editingPostId = -1;
            btnSavePost.Content = "Als Entwurf speichern";

            ValidateInput();
        }

        private void ValidateInput()
        {
            if (btnSavePost == null) return;

            bool hasText = !string.IsNullOrWhiteSpace(txtPostContent.Text);
            bool hasImage = !string.IsNullOrEmpty(_currentSelectedImagePath);

            btnSavePost.IsEnabled = hasText && hasImage;
        }

        private void UpdatePostPreview()
        {
            string fullText = txtPostContent.Text;
            string headline = GetFirstLine(fullText);

            if (lblPreviewHeadline != null)
                lblPreviewHeadline.Text = string.IsNullOrWhiteSpace(headline) ? "Deine Headline..." : headline;

            if (lblPreviewText != null)
                lblPreviewText.Text = string.IsNullOrWhiteSpace(fullText) ? "Hier erscheint dein Text..." : fullText;
        }

        private void ShowImageInPreview(string filePath)
        {
            if (string.IsNullOrEmpty(filePath) || !File.Exists(filePath))
            {
                imgPreview.Source = null;
                imgEditorPreview.Source = null;
                return;
            }

            try
            {
                BitmapImage bitmap = new BitmapImage();
                bitmap.BeginInit();
                bitmap.UriSource = new Uri(filePath);
                bitmap.CacheOption = BitmapCacheOption.OnLoad; // Datei nicht sperren
                bitmap.EndInit();

                imgPreview.Source = bitmap;
                imgEditorPreview.Source = bitmap;
            }
            catch
            {
                imgPreview.Source = null;
                imgEditorPreview.Source = null;
            }
        }

        private void LoadSettingsToUI()
        {
            try
            {
                var settings = DatabaseHelper.LoadSettings();
                if (settings != null)
                {
                    txtAppId.Text = settings.AppId;
                    txtPageId.Text = settings.PageId;
                    txtAccessToken.Text = settings.AccessToken;
                }
            }
            catch { /* Ignorieren */ }
        }

        private string GetFirstLine(string text)
        {
            if (string.IsNullOrEmpty(text)) return "";
            using (StringReader reader = new StringReader(text))
            {
                return reader.ReadLine() ?? "";
            }
        }

        private void BtnDeletePost_Click(object sender, RoutedEventArgs e)
        {
            // 1. Prüfen: Wurde eine Zeile ausgewählt?
            if (gridPosts.SelectedItem is Post selectedPost)
            {
                // 2. Sicherheitsfrage stellen
                var result = MessageBox.Show(
                    $"Möchtest du den Beitrag '{selectedPost.Headline}' wirklich löschen?\nDas Bild wird ebenfalls von der Festplatte entfernt.",
                    "Löschen bestätigen",
                    MessageBoxButton.YesNo,
                    MessageBoxImage.Warning);

                if (result == MessageBoxResult.Yes)
                {
                    try
                    {
                        // 3. Bild-Datei löschen (Müllvermeidung)
                        if (!string.IsNullOrEmpty(selectedPost.ImagePath) && File.Exists(selectedPost.ImagePath))
                        {
                            // Nur löschen, wenn das Bild im App-Ordner liegt (Sicherheitscheck)
                            if (selectedPost.ImagePath.Contains(AppDomain.CurrentDomain.BaseDirectory))
                            {
                                // Trick: Speicher aufräumen, falls das Bild noch irgendwo "festgehalten" wird
                                GC.Collect();
                                GC.WaitForPendingFinalizers();

                                File.Delete(selectedPost.ImagePath);
                            }
                        }

                        // 4. Aus Datenbank löschen
                        DatabaseHelper.DeletePost(selectedPost.Id);

                        // 5. Falls wir diesen Post gerade im Editor offen hatten -> Editor leeren
                        // (Sonst könnte man versehentlich einen gelöschten Post wieder speichern)
                        if (_editingPostId == selectedPost.Id)
                        {
                            ClearEditor();
                        }

                        // 6. Liste neu laden
                        LoadPostsList();

                        MessageBox.Show("Beitrag wurde erfolgreich gelöscht.");
                    }
                    catch (Exception ex)
                    {
                        MessageBox.Show($"Fehler beim Löschen: {ex.Message}", "Fehler", MessageBoxButton.OK, MessageBoxImage.Error);
                    }
                }
            }
            else
            {
                MessageBox.Show("Bitte wähle zuerst einen Beitrag aus der Liste aus.", "Keine Auswahl");
            }
        }

        private async void BtnTestConnection_Click(object sender, RoutedEventArgs e)
        {
            // 1. Daten aus den Textfeldern holen
            string pageId = txtPageId.Text.Trim();
            string token = txtAccessToken.Text.Trim();

            if (string.IsNullOrEmpty(pageId) || string.IsNullOrEmpty(token))
            {
                MessageBox.Show("Bitte gib erst Page-ID und Access Token ein.");
                return;
            }

            // 2. Button kurz deaktivieren (User Feedback)
            if (btnTestConnection != null)
            {
                btnTestConnection.IsEnabled = false;
                btnTestConnection.Content = "Prüfe...";
            }

            if (lblStatus != null) lblStatus.Text = "Verbinde mit Facebook...";

            try
            {
                // 3. API Service aufrufen (wartet hier dank "await")
                // Falls "FacebookApiService" rot ist, fehlt oben: using Facebook_Assistent.Services;
                string pageName = await FacebookApiService.ValidateConnection(pageId, token);

                // 4. Erfolg!
                MessageBox.Show($"Erfolg! Verbunden mit Seite:\n'{pageName}'",
                                "Verbindung OK", MessageBoxButton.OK, MessageBoxImage.Information);

                if (lblStatus != null) lblStatus.Text = $"Verbunden mit: {pageName}";
            }
            catch (Exception ex)
            {
                // 5. Fehler
                MessageBox.Show($"Verbindung fehlgeschlagen:\n{ex.Message}",
                                "Fehler", MessageBoxButton.OK, MessageBoxImage.Error);

                if (lblStatus != null) lblStatus.Text = "Verbindung fehlgeschlagen.";
            }
            finally
            {
                // 6. Aufräumen (Button wieder aktivieren)
                if (btnTestConnection != null)
                {
                    btnTestConnection.IsEnabled = true;
                    btnTestConnection.Content = "Verbindung testen";
                }
            }
        }

        private async void btnPublishNow_Click(object sender, RoutedEventArgs e)
        {
            // 1. Welcher Post ist ausgewählt?
            if (gridPosts.SelectedItem is Post selectedPost)
            {
                // Sicherheitschecks
                if (selectedPost.Status == 1)
                {
                    MessageBox.Show("Dieser Beitrag ist bereits veröffentlicht!");
                    return;
                }

                if (MessageBox.Show($"Diesen Beitrag jetzt LIVE auf Facebook posten?", "Veröffentlichen", MessageBoxButton.YesNo, MessageBoxImage.Question) != MessageBoxResult.Yes)
                {
                    return;
                }

                // 2. Einstellungen laden (Token & Page ID)
                var settings = DatabaseHelper.LoadSettings();
                if (settings == null || string.IsNullOrEmpty(settings.AccessToken))
                {
                    MessageBox.Show("Bitte konfiguriere erst die Einstellungen (Tab 1)!");
                    return;
                }

                // GUI Feedback: Button sperren, Warte-Cursor
                btnPublishNow.IsEnabled = false;
                Mouse.OverrideCursor = Cursors.Wait;
                lblStatus.Text = "Sende Daten an Facebook... Bitte warten...";

                try
                {
                    // 3. SERVICE AUFRUF: Upload zu Facebook
                    string newFbId = await FacebookApiService.PublishPhotoPost(
                        settings.PageId,
                        settings.AccessToken,
                        selectedPost.FullText,
                        selectedPost.ImagePath
                    );

                    // 4. DB UPDATE: Lokal als veröffentlicht markieren
                    DatabaseHelper.MarkAsPublished(selectedPost.Id, newFbId);

                    // 5. Erfolg!
                    MessageBox.Show($"Erfolgreich veröffentlicht!\nFB-ID: {newFbId}", "Gepostet", MessageBoxButton.OK, MessageBoxImage.Information);

                    // Liste aktualisieren, damit der Status von "Entwurf" auf "Veröffentlicht" springt
                    LoadPostsList();
                    lblStatus.Text = "Veröffentlichung erfolgreich.";
                }
                catch (Exception ex)
                {
                    MessageBox.Show($"Fehler bei der Veröffentlichung:\n{ex.Message}", "Fehler", MessageBoxButton.OK, MessageBoxImage.Error);
                    lblStatus.Text = "Veröffentlichung abgebrochen.";
                }
                finally
                {
                    // GUI wieder freigeben
                    btnPublishNow.IsEnabled = true;
                    Mouse.OverrideCursor = null;
                }
            }
            else
            {
                MessageBox.Show("Bitte wähle einen Entwurf aus der Liste aus.");
            }
        }

        private async void BtnLoadComments_Click(object sender, RoutedEventArgs e)
        {
            if (!(gridStats.SelectedItem is StatsPostRow selectedRow))
            {
                MessageBox.Show("Bitte wähle zuerst einen Beitrag in der Statistik aus.");
                return;
            }

            if (string.IsNullOrWhiteSpace(selectedRow.FacebookPostId))
            {
                MessageBox.Show("Der ausgewählte Beitrag hat keine Facebook-ID und kann nicht geladen werden.");
                return;
            }

            try
            {
                btnLoadComments.IsEnabled = false;
                lblStatus.Text = "Lade Kommentare...";

                var settings = DatabaseHelper.LoadSettings();
                if (settings == null || string.IsNullOrWhiteSpace(settings.AccessToken))
                {
                    MessageBox.Show("Bitte zuerst Access Token in den Einstellungen speichern.");
                    return;
                }

                var comments = await FacebookApiService.GetPostComments(selectedRow.FacebookPostId, settings.AccessToken);

                var rows = comments
                    .Select((c, index) => new CommentRow
                    {
                        Index = index + 1,
                        Author = c.AuthorName,
                        Message = c.Message,
                        CreatedDisplay = c.CreatedTime.HasValue ? c.CreatedTime.Value.ToString("dd.MM.yyyy HH:mm") : "-"
                    })
                    .ToList();

                gridComments.ItemsSource = rows;
                lblCommentsTitle.Text = $"Kommentare zu: {selectedRow.Headline}";
                lblCommentsCount.Text = $"{rows.Count} Kommentare geladen";
                tabComments.IsSelected = true;
                lblStatus.Text = "Kommentare geladen.";
            }
            catch (Exception ex)
            {
                MessageBox.Show($"Fehler beim Laden der Kommentare: {ex.Message}");
                lblStatus.Text = "Fehler beim Laden der Kommentare.";
            }
            finally
            {
                btnLoadComments.IsEnabled = true;
            }
        }

        private async void btnRefreshStats_Click(object sender, RoutedEventArgs e)
        {
            // 1. Einstellungen & Verbindung prüfen
            var settings = DatabaseHelper.LoadSettings();
            if (settings == null || string.IsNullOrEmpty(settings.AccessToken))
            {
                MessageBox.Show("Keine Zugangsdaten gefunden.");
                return;
            }

            btnRefreshStats.IsEnabled = false;
            lblStatus.Text = "Lade Statistik-Daten...";

            try
            {
                // 2. Nur VERÖFFENTLICHTE Posts holen (Status == 1)
                // Wir holen erst alle und filtern dann mit LINQ
                var allPosts = DatabaseHelper.GetPosts();
                var publishedPosts = allPosts.Where(p => p.Status == 1 && !string.IsNullOrEmpty(p.FacebookPostId)).ToList();

                if (publishedPosts.Count == 0)
                {
                    MessageBox.Show("Noch keine veröffentlichten Beiträge vorhanden.");
                    return;
                }

                // 3. Schleife durch alle Posts (API Abfragen)
                int processed = 0;
                foreach (var post in publishedPosts)
                {
                    // Update im Status anzeigen
                    processed++;
                    lblStatus.Text = $"Aktualisiere Post {processed} von {publishedPosts.Count}...";

                    // API Call
                    var (likes, comments, shares) = await FacebookApiService.GetPostStatistics(post.FacebookPostId, settings.AccessToken);

                    // Wenn -1 zurückkommt, gab es einen Fehler (z.B. Post auf FB gelöscht), wir ignorieren das hier einfach
                    if (likes >= 0)
                    {
                        // DB Update
                        DatabaseHelper.UpdatePostStats(post.Id, likes, comments, shares);
                    }
                }

                // 4. GUI aktualisieren (Daten neu aus DB laden)
                UpdateStatisticsUI();

                MessageBox.Show("Statistiken erfolgreich aktualisiert!", "Fertig", MessageBoxButton.OK, MessageBoxImage.Information);
                lblStatus.Text = "Statistik aktuell.";
            }
            catch (Exception ex)
            {
                MessageBox.Show($"Fehler beim Abrufen: {ex.Message}");
            }
            finally
            {
                btnRefreshStats.IsEnabled = true;
            }
        }

        // Hilfsmethode, um die UI-Elemente in Tab 4 zu füllen
        private void UpdateStatisticsUI()
        {
            var allPosts = DatabaseHelper.GetPosts();
            var publishedPosts = allPosts
                .Where(p => p.Status == 1)
                .ToList();

            int totalLikes = publishedPosts.Sum(p => p.LikesCount);
            int totalComments = publishedPosts.Sum(p => p.CommentsCount);
            int totalShares = publishedPosts.Sum(p => p.SharesCount);
            int totalInteractions = totalLikes + totalComments + totalShares;

            lblTotalLikes.Text = totalLikes.ToString();
            lblTotalComments.Text = totalComments.ToString();
            lblTotalShares.Text = totalShares.ToString();
            lblTotalInteractions.Text = totalInteractions.ToString();
            lblPublishedPosts.Text = publishedPosts.Count.ToString();
            lblDraftPosts.Text = allPosts.Count(p => p.Status == 0).ToString();

            double avgInteractions = publishedPosts.Count > 0
                ? (double)totalInteractions / publishedPosts.Count
                : 0;
            lblAvgInteractions.Text = avgInteractions.ToString("0.0", CultureInfo.InvariantCulture);

            var rankedPosts = publishedPosts
                .Select(p =>
                {
                    int interactions = p.LikesCount + p.CommentsCount + p.SharesCount;
                    int score = p.LikesCount + (p.CommentsCount * 2) + (p.SharesCount * 3);
                    int daysOnline = 0;

                    if (p.PublishedDate.HasValue)
                    {
                        daysOnline = Math.Max(1, (int)Math.Ceiling((DateTime.Now - p.PublishedDate.Value).TotalDays));
                    }

                    string interactionsPerDay = daysOnline > 0
                        ? (interactions / (double)daysOnline).ToString("0.00", CultureInfo.InvariantCulture)
                        : "-";

                    return new
                    {
                        Post = p,
                        Interactions = interactions,
                        Score = score,
                        DaysOnline = daysOnline,
                        InteractionsPerDay = interactionsPerDay
                    };
                })
                .OrderByDescending(x => x.Score)
                .ThenByDescending(x => x.Interactions)
                .ToList();

            if (rankedPosts.Any())
            {
                var topPost = rankedPosts.First();
                lblTopPostHeadline.Text = topPost.Post.Headline;
                lblTopPostScore.Text = $"Score: {topPost.Score} | Interaktionen: {topPost.Interactions}";
            }
            else
            {
                lblTopPostHeadline.Text = "-";
                lblTopPostScore.Text = "Score: 0";
            }

            DateTime now = DateTime.Now;
            var posts7 = publishedPosts.Where(p => p.PublishedDate.HasValue && p.PublishedDate.Value >= now.AddDays(-7)).ToList();
            var posts30 = publishedPosts.Where(p => p.PublishedDate.HasValue && p.PublishedDate.Value >= now.AddDays(-30)).ToList();

            int interactions7 = posts7.Sum(p => p.LikesCount + p.CommentsCount + p.SharesCount);
            int interactions30 = posts30.Sum(p => p.LikesCount + p.CommentsCount + p.SharesCount);

            lblLast7Days.Text = $"{posts7.Count} Posts | {interactions7} Interaktionen";
            lblLast30Days.Text = $"{posts30.Count} Posts | {interactions30} Interaktionen";

            int syncedPosts = publishedPosts.Count(p => !string.IsNullOrWhiteSpace(p.FacebookPostId));
            lblDataQuality.Text = $"{syncedPosts}/{publishedPosts.Count} veröffentlichten Posts mit FB-ID verknüpft";

            var rows = rankedPosts
                .Select((x, index) => new StatsPostRow
                {
                    Rank = index + 1,
                    Headline = x.Post.Headline,
                    PublishedDisplay = x.Post.PublishedDate?.ToString("dd.MM.yyyy") ?? "-",
                    Likes = x.Post.LikesCount,
                    Comments = x.Post.CommentsCount,
                    Shares = x.Post.SharesCount,
                    Interactions = x.Interactions,
                    EngagementScore = x.Score,
                    InteractionsPerDay = x.InteractionsPerDay,
                    DaysOnline = x.DaysOnline,
                    FacebookPostId = x.Post.FacebookPostId
                })
                .ToList();

            gridStats.ItemsSource = rows;
        }
    }
}
