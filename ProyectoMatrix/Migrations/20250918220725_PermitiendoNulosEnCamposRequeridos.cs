using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace ProyectoMatrix.Migrations
{
    /// <inheritdoc />
    public partial class PermitiendoNulosEnCamposRequeridos : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "AuditoriasUsuarios",
                columns: table => new
                {
                    AuditoriaID = table.Column<int>(type: "int", nullable: false),
                    DescripcionDelCambio = table.Column<string>(type: "nvarchar(max)", nullable: false),
                    FechaModificacion = table.Column<DateTime>(type: "datetime2", nullable: false),
                    ModificadoPor = table.Column<string>(type: "nvarchar(max)", nullable: false)
                },
                constraints: table =>
                {
                });

            migrationBuilder.CreateTable(
                name: "Comunicados",
                columns: table => new
                {
                    ComunicadoID = table.Column<int>(type: "int", nullable: false)
                        .Annotation("SqlServer:Identity", "1, 1"),
                    Nombre = table.Column<string>(type: "nvarchar(max)", nullable: false),
                    Descripcion = table.Column<string>(type: "nvarchar(max)", nullable: true),
                    Imagen = table.Column<string>(type: "nvarchar(max)", nullable: true),
                    FechaCreacion = table.Column<DateTime>(type: "datetime2", nullable: false),
                    UsuarioCreadorID = table.Column<int>(type: "int", nullable: true),
                    EsPublico = table.Column<bool>(type: "bit", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_Comunicados", x => x.ComunicadoID);
                });

            migrationBuilder.CreateTable(
                name: "Empresas",
                columns: table => new
                {
                    EmpresaID = table.Column<int>(type: "int", nullable: false)
                        .Annotation("SqlServer:Identity", "1, 1"),
                    Nombre = table.Column<string>(type: "nvarchar(max)", nullable: false),
                    Activa = table.Column<bool>(type: "bit", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_Empresas", x => x.EmpresaID);
                });

            migrationBuilder.CreateTable(
                name: "Notificaciones",
                columns: table => new
                {
                    Id = table.Column<int>(type: "int", nullable: false)
                        .Annotation("SqlServer:Identity", "1, 1"),
                    Tipo = table.Column<string>(type: "nvarchar(max)", nullable: false),
                    Titulo = table.Column<string>(type: "nvarchar(max)", nullable: false),
                    Mensaje = table.Column<string>(type: "nvarchar(max)", nullable: true),
                    IdOrigen = table.Column<int>(type: "int", nullable: false),
                    TablaOrigen = table.Column<string>(type: "nvarchar(max)", nullable: false),
                    UsuarioId = table.Column<int>(type: "int", nullable: true),
                    EmpresaId = table.Column<int>(type: "int", nullable: true),
                    FechaCreacion = table.Column<DateTime>(type: "datetime2", nullable: false),
                    FechaExpiracion = table.Column<DateTime>(type: "datetime2", nullable: false),
                    EsLeida = table.Column<bool>(type: "bit", nullable: false),
                    FechaEliminacion = table.Column<DateTime>(type: "datetime2", nullable: true),
                    EsArchivada = table.Column<bool>(type: "bit", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_Notificaciones", x => x.Id);
                });

            migrationBuilder.CreateTable(
                name: "Persona",
                columns: table => new
                {
                    PersonaID = table.Column<int>(type: "int", nullable: false)
                        .Annotation("SqlServer:Identity", "1, 1"),
                    Nombre = table.Column<string>(type: "nvarchar(max)", nullable: true),
                    ApellidoPaterno = table.Column<string>(type: "nvarchar(max)", nullable: true),
                    ApellidoMaterno = table.Column<string>(type: "nvarchar(max)", nullable: true),
                    Correo = table.Column<string>(type: "nvarchar(max)", nullable: true),
                    Telefono = table.Column<string>(type: "nvarchar(max)", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_Persona", x => x.PersonaID);
                });

            migrationBuilder.CreateTable(
                name: "Roles",
                columns: table => new
                {
                    RolID = table.Column<int>(type: "int", nullable: false)
                        .Annotation("SqlServer:Identity", "1, 1"),
                    URol = table.Column<string>(type: "nvarchar(max)", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_Roles", x => x.RolID);
                });

            migrationBuilder.CreateTable(
                name: "UsuarioModel",
                columns: table => new
                {
                    UsuarioID = table.Column<int>(type: "int", nullable: false)
                        .Annotation("SqlServer:Identity", "1, 1"),
                    PersonaID = table.Column<int>(type: "int", nullable: false),
                    EmpresaID = table.Column<int>(type: "int", nullable: false),
                    Username = table.Column<string>(type: "nvarchar(max)", nullable: false),
                    Password = table.Column<string>(type: "nvarchar(max)", nullable: false),
                    Nombre = table.Column<string>(type: "nvarchar(max)", nullable: false),
                    Apellido = table.Column<string>(type: "nvarchar(max)", nullable: false),
                    Correo = table.Column<string>(type: "nvarchar(max)", nullable: false),
                    Telefono = table.Column<string>(type: "nvarchar(max)", nullable: false),
                    NombreEmpresa = table.Column<string>(type: "nvarchar(max)", nullable: false),
                    ColorPrimario = table.Column<string>(type: "nvarchar(max)", nullable: false),
                    Logo = table.Column<string>(type: "nvarchar(max)", nullable: false),
                    Rol = table.Column<string>(type: "nvarchar(max)", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_UsuarioModel", x => x.UsuarioID);
                });

            migrationBuilder.CreateTable(
                name: "Webinars",
                columns: table => new
                {
                    WebinarID = table.Column<int>(type: "int", nullable: false)
                        .Annotation("SqlServer:Identity", "1, 1"),
                    Titulo = table.Column<string>(type: "nvarchar(max)", nullable: false),
                    Descripcion = table.Column<string>(type: "nvarchar(max)", nullable: false),
                    FechaInicio = table.Column<DateTime>(type: "datetime2", nullable: false),
                    FechaFin = table.Column<DateTime>(type: "datetime2", nullable: false),
                    EsPublico = table.Column<bool>(type: "bit", nullable: false),
                    UrlTeams = table.Column<string>(type: "nvarchar(max)", nullable: true),
                    Imagen = table.Column<string>(type: "nvarchar(max)", nullable: true),
                    UsuarioCreadorID = table.Column<int>(type: "int", nullable: true),
                    FechaCreacion = table.Column<DateTime>(type: "datetime2", nullable: false, defaultValueSql: "SYSUTCDATETIME()")
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_Webinars", x => x.WebinarID);
                });

            migrationBuilder.CreateTable(
                name: "ComunicadosEmpresas",
                columns: table => new
                {
                    ComunicadoID = table.Column<int>(type: "int", nullable: false),
                    EmpresaID = table.Column<int>(type: "int", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_ComunicadosEmpresas", x => new { x.ComunicadoID, x.EmpresaID });
                    table.ForeignKey(
                        name: "FK_ComunicadosEmpresas_Comunicados_ComunicadoID",
                        column: x => x.ComunicadoID,
                        principalTable: "Comunicados",
                        principalColumn: "ComunicadoID",
                        onDelete: ReferentialAction.Cascade);
                    table.ForeignKey(
                        name: "FK_ComunicadosEmpresas_Empresas_EmpresaID",
                        column: x => x.EmpresaID,
                        principalTable: "Empresas",
                        principalColumn: "EmpresaID",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateTable(
                name: "NotificacionEmpresas",
                columns: table => new
                {
                    Id = table.Column<int>(type: "int", nullable: false)
                        .Annotation("SqlServer:Identity", "1, 1"),
                    NotificacionId = table.Column<int>(type: "int", nullable: false),
                    EmpresaId = table.Column<int>(type: "int", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_NotificacionEmpresas", x => x.Id);
                    table.ForeignKey(
                        name: "FK_NotificacionEmpresas_Notificaciones_NotificacionId",
                        column: x => x.NotificacionId,
                        principalTable: "Notificaciones",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateTable(
                name: "NotificacionLecturas",
                columns: table => new
                {
                    Id = table.Column<int>(type: "int", nullable: false)
                        .Annotation("SqlServer:Identity", "1, 1"),
                    NotificacionId = table.Column<int>(type: "int", nullable: false),
                    UsuarioId = table.Column<int>(type: "int", nullable: false),
                    FechaLeida = table.Column<DateTime>(type: "datetime2", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_NotificacionLecturas", x => x.Id);
                    table.ForeignKey(
                        name: "FK_NotificacionLecturas_Notificaciones_NotificacionId",
                        column: x => x.NotificacionId,
                        principalTable: "Notificaciones",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateTable(
                name: "Usuarios",
                columns: table => new
                {
                    UsuarioID = table.Column<int>(type: "int", nullable: false)
                        .Annotation("SqlServer:Identity", "1, 1"),
                    Username = table.Column<string>(type: "nvarchar(max)", nullable: true),
                    Contrasena = table.Column<string>(type: "nvarchar(max)", nullable: true),
                    RolID = table.Column<int>(type: "int", nullable: false),
                    PersonaID = table.Column<int>(type: "int", nullable: false),
                    Activo = table.Column<bool>(type: "bit", nullable: false),
                    FechaCreacion = table.Column<DateTime>(type: "datetime2", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_Usuarios", x => x.UsuarioID);
                    table.ForeignKey(
                        name: "FK_Usuarios_Persona_PersonaID",
                        column: x => x.PersonaID,
                        principalTable: "Persona",
                        principalColumn: "PersonaID",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateTable(
                name: "EmpresaModel",
                columns: table => new
                {
                    EmpresaID = table.Column<int>(type: "int", nullable: false)
                        .Annotation("SqlServer:Identity", "1, 1"),
                    Nombre = table.Column<string>(type: "nvarchar(max)", nullable: false),
                    Logo = table.Column<string>(type: "nvarchar(max)", nullable: false),
                    ColorPrimario = table.Column<string>(type: "nvarchar(max)", nullable: false),
                    UsuarioModelUsuarioID = table.Column<int>(type: "int", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_EmpresaModel", x => x.EmpresaID);
                    table.ForeignKey(
                        name: "FK_EmpresaModel_UsuarioModel_UsuarioModelUsuarioID",
                        column: x => x.UsuarioModelUsuarioID,
                        principalTable: "UsuarioModel",
                        principalColumn: "UsuarioID");
                });

            migrationBuilder.CreateTable(
                name: "UsuariosEmpresas",
                columns: table => new
                {
                    UsuarioID = table.Column<int>(type: "int", nullable: false)
                        .Annotation("SqlServer:Identity", "1, 1"),
                    EmpresaID = table.Column<int>(type: "int", nullable: false),
                    Activo = table.Column<bool>(type: "bit", nullable: false),
                    UsuarioTempId = table.Column<int>(type: "int", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_UsuariosEmpresas", x => x.UsuarioID);
                    table.ForeignKey(
                        name: "FK_UsuariosEmpresas_Empresas_EmpresaID",
                        column: x => x.EmpresaID,
                        principalTable: "Empresas",
                        principalColumn: "EmpresaID",
                        onDelete: ReferentialAction.Cascade);
                    table.ForeignKey(
                        name: "FK_UsuariosEmpresas_UsuarioModel_UsuarioTempId",
                        column: x => x.UsuarioTempId,
                        principalTable: "UsuarioModel",
                        principalColumn: "UsuarioID",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateTable(
                name: "WebinarsEmpresas",
                columns: table => new
                {
                    WebinarID = table.Column<int>(type: "int", nullable: false),
                    EmpresaID = table.Column<int>(type: "int", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_WebinarsEmpresas", x => new { x.WebinarID, x.EmpresaID });
                    table.ForeignKey(
                        name: "FK_WebinarsEmpresas_Empresas_EmpresaID",
                        column: x => x.EmpresaID,
                        principalTable: "Empresas",
                        principalColumn: "EmpresaID",
                        onDelete: ReferentialAction.Cascade);
                    table.ForeignKey(
                        name: "FK_WebinarsEmpresas_Webinars_WebinarID",
                        column: x => x.WebinarID,
                        principalTable: "Webinars",
                        principalColumn: "WebinarID",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateIndex(
                name: "IX_ComunicadosEmpresas_EmpresaID",
                table: "ComunicadosEmpresas",
                column: "EmpresaID");

            migrationBuilder.CreateIndex(
                name: "IX_EmpresaModel_UsuarioModelUsuarioID",
                table: "EmpresaModel",
                column: "UsuarioModelUsuarioID");

            migrationBuilder.CreateIndex(
                name: "IX_NotificacionEmpresas_EmpresaId_NotificacionId",
                table: "NotificacionEmpresas",
                columns: new[] { "EmpresaId", "NotificacionId" });

            migrationBuilder.CreateIndex(
                name: "IX_NotificacionEmpresas_NotificacionId",
                table: "NotificacionEmpresas",
                column: "NotificacionId");

            migrationBuilder.CreateIndex(
                name: "IX_NotificacionLecturas_NotificacionId_UsuarioId",
                table: "NotificacionLecturas",
                columns: new[] { "NotificacionId", "UsuarioId" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_Usuarios_PersonaID",
                table: "Usuarios",
                column: "PersonaID");

            migrationBuilder.CreateIndex(
                name: "IX_UsuariosEmpresas_EmpresaID",
                table: "UsuariosEmpresas",
                column: "EmpresaID");

            migrationBuilder.CreateIndex(
                name: "IX_UsuariosEmpresas_UsuarioTempId",
                table: "UsuariosEmpresas",
                column: "UsuarioTempId");

            migrationBuilder.CreateIndex(
                name: "IX_WebinarsEmpresas_EmpresaID",
                table: "WebinarsEmpresas",
                column: "EmpresaID");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "AuditoriasUsuarios");

            migrationBuilder.DropTable(
                name: "ComunicadosEmpresas");

            migrationBuilder.DropTable(
                name: "EmpresaModel");

            migrationBuilder.DropTable(
                name: "NotificacionEmpresas");

            migrationBuilder.DropTable(
                name: "NotificacionLecturas");

            migrationBuilder.DropTable(
                name: "Roles");

            migrationBuilder.DropTable(
                name: "Usuarios");

            migrationBuilder.DropTable(
                name: "UsuariosEmpresas");

            migrationBuilder.DropTable(
                name: "WebinarsEmpresas");

            migrationBuilder.DropTable(
                name: "Comunicados");

            migrationBuilder.DropTable(
                name: "Notificaciones");

            migrationBuilder.DropTable(
                name: "Persona");

            migrationBuilder.DropTable(
                name: "UsuarioModel");

            migrationBuilder.DropTable(
                name: "Empresas");

            migrationBuilder.DropTable(
                name: "Webinars");
        }
    }
}
