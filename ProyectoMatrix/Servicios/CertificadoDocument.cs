using QuestPDF.Fluent;
using QuestPDF.Helpers;
using QuestPDF.Infrastructure;
using System;
using System.Globalization;
using System.IO;

namespace ProyectoMatrix.Servicios
{
    public class CertificadoDocument : IDocument
    {
        private readonly string _nombre;
        private readonly string _curso;
        private readonly DateTime _fecha;
        private readonly byte[] _logo;

        static CertificadoDocument()
        {
            QuestPDF.Settings.License = LicenseType.Community;
        }

        public CertificadoDocument(string nombre, string curso, DateTime fecha, byte[] logo)
        {
            _nombre = nombre;
            _curso = curso;
            _fecha = fecha;
            _logo = logo;
        }

        public DocumentMetadata GetMetadata() => DocumentMetadata.Default;

        public void Compose(IDocumentContainer container)
        {
            container.Page(page =>
            {
                page.Size(PageSizes.A4.Landscape());
                page.Margin(50);

                // Fondo blanco
                page.Background(Colors.White);

                // Contenido
                page.Content().Column(column =>
                {
                    column.Spacing(20);

                    // Header con logo NS Group
                    column.Item().Row(row =>
                    {
                        row.RelativeItem().AlignLeft().Height(60).Image(_logo);
                    });

                    column.Item().AlignCenter().Text("NS GROUP OTORGA EL PRESENTE")
                        .FontSize(14).FontColor(Colors.Grey.Medium).LetterSpacing(2);

                    column.Item().AlignCenter().Text("RECONOCIMIENTO")
                        .FontSize(48).FontColor(Colors.Blue.Medium).Bold();

                    column.Item().AlignCenter().Text($"A: {_nombre.ToUpper()}")
                        .FontSize(28).FontColor(Colors.Grey.Darken3).Bold();

                    column.Item().AlignCenter().Text($"Por su participación en {_curso}")
                        .FontSize(18).FontColor(Colors.Grey.Medium);

                    // Firmas
                    column.Item().Row(row =>
                    {
                        row.RelativeItem().AlignLeft().Column(c =>
                        {
                            c.Item().Text("_________________________");
                            c.Item().Text("YADIRA I. OLGUÍN MIJARES").Bold();
                            c.Item().Text("Directora de Recursos Humanos").FontSize(12);
                        });

                        row.RelativeItem().AlignRight().Column(c =>
                        {
                            c.Item().Text("_________________________");
                            c.Item().Text("FIRMA #2").Bold();
                            c.Item().Text("Cargo").FontSize(12);
                        });
                    });

                    column.Item().AlignRight().Text(FormatearFecha(_fecha))
                        .FontSize(12).FontColor(Colors.Grey.Medium);
                });

           
            });
        }

        private string FormatearFecha(DateTime fecha)
        {
            var cultura = new CultureInfo("es-MX");
            var nombreMes = cultura.DateTimeFormat.GetMonthName(fecha.Month).ToLower();
            return $"Puebla, Pue. a {fecha.Day} de {nombreMes} {fecha.Year}";
        }

        public void GeneratePdf(string filePath)
        {
            QuestPDF.Fluent.Document.Create(container => Compose(container))
                   .GeneratePdf(filePath);
        }

        public void GeneratePdf(Stream stream)
        {
            QuestPDF.Fluent.Document.Create(container => Compose(container))
                   .GeneratePdf(stream);
        }
    }
}
