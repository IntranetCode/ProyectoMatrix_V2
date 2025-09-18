//SE CREO ESTE SERVICIO PARA CONSULTAR QUE ACCIONES TIENE ASIGNADO UN USUARIO

namespace ProyectoMatrix.Servicios
{
    public interface IServicioAcceso
    {

        Task<bool> TienePermisoAsync(int usuarioId, string subMenu, string accion);
    }

}

