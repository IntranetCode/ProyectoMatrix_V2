namespace ProyectoMatrix.Models
{
    public class MenuModel
    {
        public int MenuID { get; set; }
        public string Nombre { get; set; }
        public string Icono { get; set; }
        public string Titulo { get; set; }
        public string Url { get; set; }
        public int Orden { get; set; }
        public int? MenuPadreID { get; set; }
        public List<MenuModel> SubMenus { get; set; } = new();
    }
    public class SubmenuViewModel
    {
        public string Nombre { get; set; }
        public string Url { get; set; }
    }
}
