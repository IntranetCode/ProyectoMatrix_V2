using System.ComponentModel.DataAnnotations;

namespace ProyectoMatrix.Models
{
    public class Vehiculo
    {
        [Key]
        public int VehiculoID { get; set; }
        
        public string Marca { get; set; }
        
        public string Modelo { get; set; }
        
        public string Placas { get; set; }
        
        public decimal Capacidad { get; set; }
        
        public bool Activo { get; set; }
    }
}