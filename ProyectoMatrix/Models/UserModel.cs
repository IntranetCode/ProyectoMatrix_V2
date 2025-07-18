using System.ComponentModel.DataAnnotations;

namespace ProyectoMatrix.Models
{
    public class UserModel
    {
        [Required]
        public string Username { get; set; }

        [Required]
        [DataType(DataType.Password)]
        public string Password { get; set; }
    }
}
