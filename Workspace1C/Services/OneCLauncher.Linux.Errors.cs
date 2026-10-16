#if LINUX
namespace Workspace1C.Services
{
    public static partial class OneCLauncher
    {
        // ====================================================================
        // Обработка ошибок / логгирование
        // ====================================================================

        /// <summary>Логгер из DI (без создания жёсткой зависимости).</summary>
        private static IAppLogger? GetLogger()
        {
            try { return AppServices.GetRequiredService<IAppLogger>(); }
            catch { return null; }
        }
    }
}
#endif