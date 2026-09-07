using System;

namespace GRA0150Net.Infrastructure.Events
{
    /// <summary>
    /// Protegeix els esdeveniments de GRA0150Net contra
    /// execucions recursives provocades per operacions internes
    /// realitzades mitjançant a3ERPActiveX.
    ///
    /// Exemple:
    /// 1. L'usuari guarda un albarà.
    /// 2. GRA0150Net rep DespuesDeGuardarDocumentoV2.
    /// 3. La DLL modifica l'albarà mitjançant ActiveX.
    /// 4. ActiveX torna a guardar el document.
    /// 5. a3ERP torna a disparar els esdeveniments.
    ///
    /// Sense aquest guard, el procés podria entrar
    /// en un bucle recursiu indefinit.
    /// </summary>
    internal static class A3ErpEventExecutionGuard
    {
        /// <summary>
        /// Profunditat d'execució interna en el fil actual.
        ///
        /// Els esdeveniments COM d'a3ERP són reentrants:
        /// una operació ActiveX pot provocar una nova crida
        /// dins del mateix fil.
        /// </summary>
        [ThreadStatic]
        private static int _profundidad;

        /// <summary>
        /// Indica si actualment s'està executant una operació
        /// interna iniciada per GRA0150Net.
        /// </summary>
        public static bool EstaActivo
        {
            get
            {
                return _profundidad > 0;
            }
        }

        /// <summary>
        /// Inicia una secció protegida contra recursivitat.
        ///
        /// El valor retornat s'ha d'utilitzar dins d'un bloc using
        /// perquè l'estat es restableixi fins i tot si es produeix
        /// una excepció.
        /// </summary>
        public static IDisposable Entrar()
        {
            _profundidad++;

            return new AmbitoEjecucion();
        }

        /// <summary>
        /// Finalitza automàticament una secció protegida.
        /// </summary>
        private sealed class AmbitoEjecucion : IDisposable
        {
            private bool _disposed;

            public void Dispose()
            {
                if (_disposed)
                {
                    return;
                }

                if (_profundidad > 0)
                {
                    _profundidad--;
                }

                _disposed = true;
            }
        }
    }
}