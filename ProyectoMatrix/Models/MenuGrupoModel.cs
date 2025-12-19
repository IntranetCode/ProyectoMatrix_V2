namespace ProyectoMatrix.Models
{
    public class MenuGrupoModel
    {
        public int MenuGrupoID { get; set; }
        public string Nombre { get; set; } = "";
        public string? Descripcion { get; set; }
        public string? IconoCss { get; set; }
        public int Orden { get; set; }
    }
}
