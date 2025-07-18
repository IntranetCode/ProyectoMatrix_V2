namespace ProyectoMatrix.Models
{

    public class MenuItemViewModel
    {
        public int MenuID { get; set; }
        public string NombreMenu { get; set; }
        public string Icono { get; set; }
        public string Url { get; set; }
        public List<SubmenuViewModel> Submenus { get; set; } = new();
    }

    


}
