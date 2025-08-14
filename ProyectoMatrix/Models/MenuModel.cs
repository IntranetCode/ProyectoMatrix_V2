/*namespace ProyectoMatrix.Models
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
*/
namespace ProyectoMatrix.Models
{
    /*    public class MenuModel
        {
            public int MenuID { get; set; }
            public string Nombre { get; set; }
            public string Icono { get; set; }
            public string Titulo { get; set; }
            public string Url { get; set; }
            public int Orden { get; set; }
            public int? MenuPadreID { get; set; }

            // Lista de submenús
            public List<MenuModel> SubMenus { get; set; } = new();

            // Lista de acciones permitidas (por ejemplo: Ver, Crear, Editar, Eliminar)
            public List<string> Acciones { get; set; } = new();
        }

        public class SubmenuViewModel
        {
            public string Nombre { get; set; }
            public string Url { get; set; }
        }*/
    public class MenuModel
    {
        public int MenuID { get; set; }
        public string Nombre { get; set; }
        public string? Icono { get; set; } // Nullable porque no lo devuelves en la consulta
        public string? Url { get; set; }
        public int Orden { get; set; }
        public int? MenuPadreID { get; set; }
        public List<MenuModel> SubMenus { get; set; } = new List<MenuModel>();
        public string Descripcion { get; set; } = string.Empty;
    }

}
