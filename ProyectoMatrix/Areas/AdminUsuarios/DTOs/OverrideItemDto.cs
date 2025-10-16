namespace ProyectoMatrix.Areas.AdminUsuarios.DTOs
{
    // Estado UI: -1 = Heredar, 0 = Denegar, 1 = Permitir

    public class OverrideItemDto
    {
        public int SubMenuID { get; set; }
        public string Nombre { get; set; } = string.Empty;
        public int Estado { get; set; }
        public bool PermisoEfectivo { get; set; }
        public int MenuID { get; set; }
        public string MenuNombre { get; set; } = "";


    }
}
