using System;
using SIL.LCModel.Core.KernelInterfaces;
using SIL.LCModel.Utils;

namespace SIL.TextEditing.LCModel
{
    /// <summary>Lets LCModel's own XML serializer, which wants an ILgWritingSystemFactory, run on a host that only
    /// supplies <see cref="IWritingSystemHandles"/>. The serializer reads handles and ids only.</summary>
    sealed class HandlesFactory : ILgWritingSystemFactory
    {
        readonly IWritingSystemHandles _handles;
        public HandlesFactory(IWritingSystemHandles handles) { _handles = handles; }

        public int GetWsFromStr(string bstr) => _handles.GetHandle(bstr);
        public string GetStrFromWs(int ws) => _handles.GetId(ws);
        public string GetIcuLocaleFromWs(int ws) => _handles.GetId(ws);
        public ILgWritingSystem get_Engine(string bstrIcuLocale) => new Engine(this, _handles.GetHandle(bstrIcuLocale), bstrIcuLocale);
        public ILgWritingSystem get_EngineOrNull(int ws) => ws == 0 ? null! : new Engine(this, ws, _handles.GetId(ws));
        public int NumberOfWs => 0;
        public void GetWritingSystems(ArrayPtr rgws, int cws) { }
        public int UserWs { get; set; }

        /// <summary>The identity of a writing system and nothing else; the serializer asks for no more.</summary>
        sealed class Engine : ILgWritingSystem
        {
            readonly ILgWritingSystemFactory _factory;
            public Engine(ILgWritingSystemFactory factory, int handle, string id)
            {
                _factory = factory;
                Handle = handle;
                Id = id;
                IcuLocale = id;
            }

            public string Id { get; }
            public int Handle { get; }
            public string IcuLocale { get; }
            public ILgWritingSystemFactory WritingSystemFactory => _factory;
            public string LanguageName => Id;
            public string SpellCheckingId => Id;
            public bool RightToLeftScript => false;
            public string DefaultFontFeatures => string.Empty;
            public string DefaultFontName => string.Empty;
            public bool UseNfcContext => false;
            public bool IsGraphiteEnabled => false;
            public bool get_IsWordForming(int ch) => false;
            public void InterpretChrp(ref LgCharRenderProps chrp) { }
        }
    }
}
