using Microsoft.AspNetCore.Http;
using System.Collections.Generic;
using System.ComponentModel.DataAnnotations;
using System.ComponentModel.DataAnnotations.Schema;

namespace ProyectoMatrix.Models
{
    [Table("ComunicadoEmpresa")]
    public class ComunicadoEmpresa
    {
       
        public int ComunicadoID { get; set; }
        public Comunicado Comunicado { get; set; } = null!;
      
        public int EmpresaID { get; set; }
        public Empresa Empresa { get; set; } = null!;
    }
    
      
    }

