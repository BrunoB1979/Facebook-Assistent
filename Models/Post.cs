using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace Facebook_Assistent.Models
{
    public class Post
    {
        public int Id { get; set; } // Primärschlüssel in DB
        public string Headline { get; set; }
        public string FullText { get; set; }
        public string ImagePath { get; set; }
        public int Status { get; set; } // 0 = Entwurf, 1 = Veröffentlicht

        // Daten von Facebook nach Veröffentlichung
        public string FacebookPostId { get; set; }
        public DateTime? PublishedDate { get; set; }

        // Statistik
        public int LikesCount { get; set; }
        public int CommentsCount { get; set; }

        // Hilfseigenschaft für die Anzeige im DataGrid (Nicht in DB speichern)
        public string StatusText => Status == 1 ? "Veröffentlicht" : "Entwurf";
        public string CreatedDate => PublishedDate.HasValue ? PublishedDate.Value.ToString("g") : "-";
    }
}
