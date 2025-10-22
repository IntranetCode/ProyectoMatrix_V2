﻿using ProyectoMatrix.Models.ModelUsuarios;
using System;
using System.Collections.Generic;
using System.ComponentModel.DataAnnotations;

namespace ProyectoMatrix.Models
{
    public class UsuarioModel
    {
        [Key]
        public int UsuarioID { get; set; }
        public int PersonaID { get; set; }
        public int EmpresaID { get; set; }
        public string Username { get; set; }



        // Contraseña hasheada en la base de datos
        public string Password { get; set; } = string.Empty;

        // Datos de la persona
        public string Nombre { get; set; } = string.Empty;
        public string Apellido { get; set; } = string.Empty;
        public string Correo { get; set; } = string.Empty;
        public string Telefono { get; set; } = string.Empty;

        // Datos de la empresa
        public string NombreEmpresa { get; set; } = string.Empty;
        public string ColorPrimario { get; set; } = string.Empty;
        public string Logo { get; set; } = string.Empty;

        // Rol del usuario (Ej. "Administrador", "Colaborador")
        public string Rol { get; set; } = string.Empty;

        // Lista de empresas asociadas (para mostrar modal si hay más de una)
        public List<EmpresaModel> Empresas { get; set; } = new List<EmpresaModel>();
    }

    /// SE LE AGREGO KEY AL EPRESA MODEL
    public class EmpresaModel
    {
        [Key]
        public int EmpresaID { get; set; }
        public string Nombre { get; set; } = string.Empty;
        public string Logo { get; set; } = string.Empty;
        public string ColorPrimario { get; set; } = string.Empty;
    }

    //Esta entidad nos pemrite obtener los datos de mi perfil de 
    //cada uno de los usuarios, esta acción de usará para que el 
    //usuario los pueda ver desde el boton Mi Perfil
    public class UsuarioPerfilViewModel
    {
        public string NombreUsuario { get; set; } = "";
        public string Username { get; set; } = "";
        public string? Correo { get; set; }
        public string? Telefono { get; set; }
        public string? NombreRol { get; set; }
        public string? DescripcionRol { get; set; }
    }
}


