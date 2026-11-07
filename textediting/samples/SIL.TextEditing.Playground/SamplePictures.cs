using System;
using System.Collections.Generic;
using System.Linq;
using Avalonia;
using Avalonia.Media;
using Avalonia.Media.Imaging;

namespace SIL.TextEditing.Playground
{
    /// <summary>One picture of the sample project: a CmPicture's GUID, the file under the project's pictures folder and its caption.</summary>
    public sealed class SamplePicture
    {
        public SamplePicture(Guid guid, string path, string caption, uint fill)
        {
            Guid = guid;
            Path = path;
            Caption = caption;
            Fill = fill;
            ObjData = SamplePictures.PictureData(guid);
        }

        public Guid Guid { get; }
        /// <summary>The file, from the project root, as FieldWorks Markdown writes it: <c>![caption](/files/pictures/red-square.png){guid=...}</c>.</summary>
        public string Path { get; }
        public string Caption { get; }
        /// <summary>The object data of the run: FieldWorks' moveable-object type (U+0008) followed by the GUID as eight UTF-16 code units.</summary>
        public string ObjData { get; }
        internal uint Fill { get; }
    }

    /// <summary>The sample project's pictures and small generated bitmaps that stand in for their files. Sample data only.</summary>
    public sealed class SamplePictures
    {
        /// <summary>FieldWorks' object-data type of a picture in text (kodtGuidMoveableObjDisp).</summary>
        public const char PictureType = (char)8;

        public static SamplePicture Red { get; } = new SamplePicture(new Guid("6f1d3a52-0c4e-4b8a-9d2f-1a7e5c3b9d01"), "/files/pictures/red-square.png", "Red square", 0xFFD04040u);
        public static SamplePicture Blue { get; } = new SamplePicture(new Guid("0b9e7c14-5a3d-4f62-8e1b-2c4d6f8a0b02"), "/files/pictures/blue-square.png", "Blue square", 0xFF4060D0u);
        public static IReadOnlyList<SamplePicture> All { get; } = new[] { Red, Blue };

        /// <summary>The object data FieldWorks stores for a picture with this GUID.</summary>
        public static string PictureData(Guid guid)
        {
            var bytes = guid.ToByteArray();
            var chars = new char[8];
            for (int i = 0; i < 8; i++) chars[i] = (char)(bytes[2 * i] | (bytes[2 * i + 1] << 8));
            return PictureType + new string(chars);
        }

        /// <summary>The sample picture an object run's data names, or null.</summary>
        public static SamplePicture? Find(string objData) => All.FirstOrDefault(p => p.ObjData == objData);

        readonly Dictionary<string, IImage> _cache = new Dictionary<string, IImage>();

        public IImage? Get(string data)
        {
            if (_cache.TryGetValue(data, out var image)) return image;
            image = Create(Find(data)?.Fill ?? 0xFF40A060u);
            _cache[data] = image;
            return image;
        }

        static IImage Create(uint fill)
        {
            var bitmap = new WriteableBitmap(new PixelSize(32, 24), new Vector(96, 96), global::Avalonia.Platform.PixelFormat.Bgra8888, global::Avalonia.Platform.AlphaFormat.Premul);
            using (var fb = bitmap.Lock())
            {
                var px = new int[32 * 24];
                for (int y = 0; y < 24; y++)
                    for (int x = 0; x < 32; x++)
                        px[y * 32 + x] = (x < 2 || y < 2 || x > 29 || y > 21) ? unchecked((int)0xFF202020) : unchecked((int)fill);
                System.Runtime.InteropServices.Marshal.Copy(px, 0, fb.Address, px.Length);
            }
            return bitmap;
        }
    }
}
