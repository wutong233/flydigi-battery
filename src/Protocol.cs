// fcs.sock 传输帧 + protobuf 解码
//   "FDG_PROTOCOL\n"   + int32le(len) + payload
//   "FDG_CHUNK\n"      + int32le(total) + int32le(n) + n*(int32le(len) + bytes)
//   "FDG_COMPRESSED\n" + int32le(len) + gzip(payload)
using System;
using System.IO;
using System.IO.Compression;
using System.Text;

namespace FlydigiBattery
{
    /// <summary>极简 protobuf 读取器（只解码本工具需要的字段）。</summary>
    internal sealed class ProtoReader
    {
        private readonly byte[] _data;
        private int _pos;
        private readonly int _end;

        public ProtoReader(byte[] data) : this(data, 0, data.Length) { }

        public ProtoReader(byte[] data, int start, int count)
        {
            _data = data;
            _pos = start;
            _end = Math.Min(data.Length, start + count);
        }

        public bool Has { get { return _pos < _end; } }

        public int Position { get { return _pos; } set { _pos = value; } }

        public ulong ReadVarint()
        {
            ulong result = 0;
            int shift = 0;
            while (_pos < _end && shift < 70)
            {
                byte b = _data[_pos++];
                result |= (ulong)(b & 0x7F) << shift;
                if ((b & 0x80) == 0) { return result; }
                shift += 7;
            }
            return result;
        }

        public void ReadTag(out int field, out int wire)
        {
            ulong key = ReadVarint();
            field = (int)(key >> 3);
            wire = (int)(key & 7);
        }

        public byte[] ReadBytes()
        {
            int len = (int)ReadVarint();
            if (len < 0 || _pos + len > _end) { len = Math.Max(0, _end - _pos); }
            byte[] r = new byte[len];
            Buffer.BlockCopy(_data, _pos, r, 0, len);
            _pos += len;
            return r;
        }

        public string ReadString() { return Encoding.UTF8.GetString(ReadBytes()); }

        /// <summary>跳过当前字段的值（调用方已经读过 tag）。</summary>
        public void SkipValue(int wire)
        {
            switch (wire)
            {
                case 0: ReadVarint(); break;
                case 1: _pos += 8; break;
                case 2: { int len = (int)ReadVarint(); _pos += len; break; }
                case 5: _pos += 4; break;
                default: _pos = _end; break;
            }
            if (_pos > _end) { _pos = _end; }
        }
    }

    /// <summary>fcs.sock 帧读写。</summary>
    internal static class FdgProtocol
    {
        private static readonly byte[] ProtocolHeader = Encoding.ASCII.GetBytes("FDG_PROTOCOL\n");
        private const int MaxFrameBytes = 32 * 1024 * 1024;

        public static void Write(Stream stream, byte[] payload)
        {
            stream.Write(ProtocolHeader, 0, ProtocolHeader.Length);
            byte[] len = BitConverter.GetBytes(payload.Length);
            stream.Write(len, 0, 4);
            stream.Write(payload, 0, payload.Length);
            stream.Flush();
        }

        /// <summary>读一帧；返回 null 表示对端已关闭。</summary>
        public static byte[] Read(Stream stream)
        {
            string header = ReadHeaderLine(stream, 64);
            if (header == null) { return null; }
            header = header.Trim();

            if (header.Length == 0) { throw new IOException("协议头为空"); }

            if (header.StartsWith("FDG_PROTOCOL", StringComparison.Ordinal))
            {
                int len = ReadInt32(stream);
                return ReadExact(stream, len);
            }

            if (header.StartsWith("FDG_CHUNK", StringComparison.Ordinal))
            {
                int total = ReadInt32(stream);
                int count = ReadInt32(stream);
                if (count <= 0 || count > 65535) { throw new IOException("分块数量异常: " + count); }
                MemoryStream ms = new MemoryStream(Math.Max(total, 0));
                for (int i = 0; i < count; i++)
                {
                    int chunkLen = ReadInt32(stream);
                    byte[] chunk = ReadExact(stream, chunkLen);
                    if (chunk == null) { throw new IOException("分块读取失败"); }
                    ms.Write(chunk, 0, chunk.Length);
                }
                return ms.ToArray();
            }

            if (header.StartsWith("FDG_COMPRESSED", StringComparison.Ordinal))
            {
                int len = ReadInt32(stream);
                byte[] gz = ReadExact(stream, len);
                if (gz == null) { throw new IOException("压缩帧读取失败"); }
                using (GZipStream gs = new GZipStream(new MemoryStream(gz), CompressionMode.Decompress))
                using (MemoryStream outMs = new MemoryStream())
                {
                    byte[] buf = new byte[8192];
                    int r;
                    while ((r = gs.Read(buf, 0, buf.Length)) > 0) { outMs.Write(buf, 0, r); }
                    return outMs.ToArray();
                }
            }

            throw new IOException("未知协议头: " + header);
        }

        private static string ReadHeaderLine(Stream stream, int maxLen)
        {
            StringBuilder sb = new StringBuilder(16);
            while (true)
            {
                int b = stream.ReadByte();
                if (b < 0) { return sb.Length > 0 ? sb.ToString() : null; }
                if (b == '\n') { return sb.ToString(); }
                sb.Append((char)b);
                if (sb.Length > maxLen) { throw new IOException("协议头过长"); }
            }
        }

        private static int ReadInt32(Stream stream)
        {
            byte[] b = ReadExact(stream, 4);
            if (b == null) { throw new IOException("读取长度失败"); }
            return BitConverter.ToInt32(b, 0);
        }

        private static byte[] ReadExact(Stream stream, int count)
        {
            if (count < 0 || count > MaxFrameBytes) { throw new IOException("长度非法: " + count); }
            byte[] buf = new byte[count];
            int off = 0;
            while (off < count)
            {
                int r = stream.Read(buf, off, count - off);
                if (r <= 0) { return null; }
                off += r;
            }
            return buf;
        }
    }
}
