using Microsoft.AspNetCore.Identity;

namespace ProyectoMatrix.Models
{
    public class ModelosComunicados
    {
    }

    public class  Empresas
    {
        public int EmpresaID { get; set; }
        public string Nombre { get; set; } 

    }

    //LA TABLA ES USUARIOS_EMPRESAS

    public class UsuariosEmpresa
    {
        public int EmpresaID { get; set; }
        public Empresa Empresa { get; set; }
        public int UsuarioID { get; set; }


    }

    public class M_Usuario
    {
                public int UsuarioID { get; set; }
        public string Nombre { get; set; }

        public string Username { get; set;}
        public string Contraseña { get; set; }

        public URol Rol { get; set; }


    }

    public class URol
    {
        public int RolID { get; set; }
        public string NombreRol { get; set; }
    }

    public class  Comunicados
    {
        public int ComunicadoID { get; set; }
        public string NombreComunicado { get; set; } 
        public string Descripcion { get; set; } 
        public string Imagen { get; set; }
        public DateTime FechaCreacion { get; set; }
        public int UsuarioCreadorID { get; set; }
        public bool EsPublico { get; set; }
        public ICollection<ComunicadoEmpresa> ComunicadoEmpresas { get; set; } 
            = new List<ComunicadoEmpresa>();

        public M_Usuario UsuarioCreador { get; set; }

    }
}
