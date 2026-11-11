using System.IO.Compression;
using System.Text;

namespace PTenoLauncher.Infrastructure.Minecraft;

public sealed record WorldVersion(string? Name, int? DataVersion);

internal static class WorldVersionReader
{
    private const int Limit = 4 * 1024 * 1024;

    public static WorldVersion Read(Stream source)
    {
        try
        {
            using var gzip = new GZipStream(source, CompressionMode.Decompress, leaveOpen: true);
            using var data = new MemoryStream();
            var buffer = new byte[8192];
            int size;
            while ((size = gzip.Read(buffer)) != 0)
            {
                if (data.Length + size > Limit) return new(null, null);
                data.Write(buffer, 0, size);
            }
            data.Position = 0;
            using var reader = new BinaryReader(data, Encoding.UTF8);
            if (reader.ReadByte() != 10) return new(null, null);
            ReadString();
            string? name = null;
            int? version = null;
            Compound(0, 0);
            return new(name, version);

            int ReadInt() => System.Buffers.Binary.BinaryPrimitives.ReverseEndianness(reader.ReadInt32());
            string ReadString()
            {
                var count = System.Buffers.Binary.BinaryPrimitives.ReverseEndianness(reader.ReadUInt16());
                var bytes = reader.ReadBytes(count);
                if (bytes.Length != count) throw new EndOfStreamException();
                return Encoding.UTF8.GetString(bytes);
            }
            void Skip(long count)
            {
                if (count < 0 || count > data.Length - data.Position) throw new InvalidDataException();
                data.Position += count;
            }
            void Compound(int depth, int scope)
            {
                if (depth > 48) throw new InvalidDataException();
                byte type;
                while ((type = reader.ReadByte()) != 0)
                {
                    var tag = ReadString();
                    if (scope == 0 && tag == "Data" && type == 10) Compound(depth + 1, 1);
                    else if (scope == 1 && tag == "Version" && type == 10) Compound(depth + 1, 2);
                    else if (scope == 1 && tag == "DataVersion" && type == 3)
                    {
                        var value = ReadInt();
                        if (value >= 0) version = value;
                    }
                    else if (scope == 2 && tag == "Name" && type == 8)
                    {
                        var value = ReadString();
                        if (value.Length is > 0 and <= 80 && !value.Any(char.IsControl)) name = value;
                    }
                    else Payload(type, depth + 1);
                }
            }
            void Payload(byte type, int depth)
            {
                if (depth > 48) throw new InvalidDataException();
                switch (type)
                {
                    case 1: Skip(1); break;
                    case 2: Skip(2); break;
                    case 3:
                    case 5: Skip(4); break;
                    case 4:
                    case 6: Skip(8); break;
                    case 7: Skip(ReadInt()); break;
                    case 8: ReadString(); break;
                    case 9:
                        var child = reader.ReadByte();
                        var count = ReadInt();
                        if (count < 0 || count > data.Length - data.Position || child == 0 && count != 0)
                            throw new InvalidDataException();
                        for (var i = 0; i < count; i++) Payload(child, depth + 1);
                        break;
                    case 10: Compound(depth, -1); break;
                    case 11: Skip((long)ReadInt() * 4); break;
                    case 12: Skip((long)ReadInt() * 8); break;
                    default: throw new InvalidDataException();
                }
            }
        }
        catch (Exception error) when (error is IOException or InvalidDataException) { return new(null, null); }
    }
}
