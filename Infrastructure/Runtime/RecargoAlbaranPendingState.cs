using System.Collections.Generic;

namespace GRA0150Net.Infrastructure.Runtime
{
    /// <summary>
    /// Tipus d'operació pendent que GRA0150Net
    /// ha de realitzar sobre la línia de recàrrec.
    /// </summary>
    internal enum TipoOperacionRecargoPendiente
    {
        Crear = 1,
        Actualizar = 2,
        Eliminar = 3
    }

    /// <summary>
    /// Representa una operació de recàrrec pendent
    /// d'executar-se després del guardat original.
    /// </summary>
    internal sealed class OperacionRecargoPendiente
    {
        public decimal IdAlbaran { get; set; }

        public TipoOperacionRecargoPendiente TipoOperacion { get; set; }

        public decimal NumeroLineaAlbaran { get; set; }

        public decimal ImporteRecargo { get; set; }
    }

    /// <summary>
    /// Manté temporalment les operacions de recàrrec
    /// entre AntesDeGuardarDocumentoV2 i
    /// DespuesDeGuardarDocumentoV2.
    ///
    /// També contempla l'alta d'un albarà nou:
    /// en el BeforeSave l'ID encara és 0,
    /// mentre que en l'AfterSave a3ERP ja proporciona
    /// l'IDALBV definitiu.
    /// </summary>
    internal sealed class RecargoAlbaranPendingState
    {
        private readonly object _syncRoot =
            new object();

        private readonly Dictionary<decimal, OperacionRecargoPendiente>
            _operacionesPendientes =
                new Dictionary<decimal, OperacionRecargoPendiente>();

        /// <summary>
        /// Operació de creació pendent corresponent
        /// a un albarà nou que encara no disposa d'ID.
        ///
        /// Només pot existir una alta pendent alhora
        /// dins del flux normal de la interfície d'a3ERP.
        /// </summary>
        private OperacionRecargoPendiente _altaPendiente;

        public void EstablecerCreacion(
            decimal idAlbaran,
            decimal importeRecargo)
        {
            if (idAlbaran <= 0m)
            {
                return;
            }

            Establecer(
                new OperacionRecargoPendiente
                {
                    IdAlbaran = idAlbaran,
                    TipoOperacion =
                        TipoOperacionRecargoPendiente.Crear,
                    NumeroLineaAlbaran = 0m,
                    ImporteRecargo = importeRecargo
                });
        }

        /// <summary>
        /// Registra la creació del recàrrec d'un albarà
        /// que encara no disposa d'IDALBV definitiu.
        /// </summary>
        public void EstablecerCreacionAltaPendiente(
            decimal importeRecargo)
        {
            lock (_syncRoot)
            {
                _altaPendiente =
                    new OperacionRecargoPendiente
                    {
                        IdAlbaran = 0m,
                        TipoOperacion =
                            TipoOperacionRecargoPendiente.Crear,
                        NumeroLineaAlbaran = 0m,
                        ImporteRecargo =
                            importeRecargo
                    };
            }
        }

        public void EstablecerActualizacion(
            decimal idAlbaran,
            decimal numeroLineaAlbaran,
            decimal importeRecargo)
        {
            if (idAlbaran <= 0m ||
                numeroLineaAlbaran <= 0m)
            {
                return;
            }

            Establecer(
                new OperacionRecargoPendiente
                {
                    IdAlbaran = idAlbaran,
                    TipoOperacion =
                        TipoOperacionRecargoPendiente.Actualizar,
                    NumeroLineaAlbaran =
                        numeroLineaAlbaran,
                    ImporteRecargo =
                        importeRecargo
                });
        }

        public void EstablecerEliminacion(
            decimal idAlbaran,
            decimal numeroLineaAlbaran)
        {
            if (idAlbaran <= 0m ||
                numeroLineaAlbaran <= 0m)
            {
                return;
            }

            Establecer(
                new OperacionRecargoPendiente
                {
                    IdAlbaran = idAlbaran,
                    TipoOperacion =
                        TipoOperacionRecargoPendiente.Eliminar,
                    NumeroLineaAlbaran =
                        numeroLineaAlbaran,
                    ImporteRecargo = 0m
                });
        }

        private void Establecer(
            OperacionRecargoPendiente operacion)
        {
            if (operacion == null ||
                operacion.IdAlbaran <= 0m)
            {
                return;
            }

            lock (_syncRoot)
            {
                _operacionesPendientes[
                    operacion.IdAlbaran] =
                    operacion;
            }
        }

        public void Eliminar(
            decimal idAlbaran)
        {
            if (idAlbaran <= 0m)
            {
                return;
            }

            lock (_syncRoot)
            {
                _operacionesPendientes.Remove(
                    idAlbaran);
            }
        }

        /// <summary>
        /// Elimina una possible alta pendent antiga.
        ///
        /// Serveix per evitar conservar estat si un guardat
        /// anterior no hagués arribat a completar-se.
        /// </summary>
        public void EliminarAltaPendiente()
        {
            lock (_syncRoot)
            {
                _altaPendiente = null;
            }
        }

        public bool TryConsumir(
            decimal idAlbaran,
            out OperacionRecargoPendiente operacion)
        {
            lock (_syncRoot)
            {
                if (!_operacionesPendientes.TryGetValue(
                    idAlbaran,
                    out operacion))
                {
                    operacion = null;
                    return false;
                }

                _operacionesPendientes.Remove(
                    idAlbaran);

                return true;
            }
        }

        /// <summary>
        /// Recupera i consumeix la creació pendent
        /// corresponent a una alta nova.
        /// </summary>
        public bool TryConsumirAltaPendiente(
            out OperacionRecargoPendiente operacion)
        {
            lock (_syncRoot)
            {
                if (_altaPendiente == null)
                {
                    operacion = null;
                    return false;
                }

                operacion = _altaPendiente;
                _altaPendiente = null;

                return true;
            }
        }

        public void Limpiar()
        {
            lock (_syncRoot)
            {
                _operacionesPendientes.Clear();
                _altaPendiente = null;
            }
        }
    }
}