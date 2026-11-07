using System;
using SIL.LCModel.Core.KernelInterfaces;

namespace SIL.TextEditing.LCModel
{
    /// <summary>The host's mapping between LCModel's integer writing-system handles and the writing-system ids that
    /// <see cref="Run.Ws"/> carries (FieldWorks passes language tags).</summary>
    public interface IWritingSystemHandles
    {
        /// <summary>The handle of a writing-system id; 0 when the id is unknown.</summary>
        int GetHandle(string wsId);

        /// <summary>The id of a handle; an empty string when the handle is 0 or unknown.</summary>
        string GetId(int handle);
    }

    /// <summary>Ready-made <see cref="IWritingSystemHandles"/>.</summary>
    public static class WritingSystemHandles
    {
        /// <summary>Uses a host's LCModel writing-system factory (<c>LcmCache.WritingSystemFactory</c> in FieldWorks).</summary>
        public static IWritingSystemHandles FromFactory(ILgWritingSystemFactory factory)
        {
            if (factory == null) throw new ArgumentNullException(nameof(factory));
            return new FactoryHandles(factory);
        }

        sealed class FactoryHandles : IWritingSystemHandles
        {
            readonly ILgWritingSystemFactory _factory;
            public FactoryHandles(ILgWritingSystemFactory factory) { _factory = factory; }
            public int GetHandle(string wsId) => _factory.GetWsFromStr(wsId);
            public string GetId(int handle)
            {
                if (handle == 0) return string.Empty;
                try
                {
                    return _factory.GetStrFromWs(handle) ?? string.Empty;
                }
                catch (Exception)
                {
                    return string.Empty;   // a real factory may throw for a handle it does not know
                }
            }
        }
    }
}
