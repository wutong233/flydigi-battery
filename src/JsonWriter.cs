// 极简 JSON 输出（不引入 Newtonsoft 之类的依赖，exe 保持单文件、零依赖）
using System;
using System.Globalization;
using System.Text;

namespace FlydigiBattery
{
    internal static class JsonWriter
    {
        public static string Status(Status s)
        {
            StringBuilder sb = new StringBuilder(512);
            sb.Append('{');
            bool first = true;
            BoolField(sb, ref first, "ok", s.Ok);
            BoolField(sb, ref first, "connected", s.Connected);
            StringField(sb, ref first, "connectionSource", s.ConnectionSource);
            StringField(sb, ref first, "source", string.IsNullOrEmpty(s.Source) ? null : s.Source);
            StringField(sb, ref first, "timestamp", s.Timestamp.ToString("s", CultureInfo.InvariantCulture));
            StringField(sb, ref first, "error", s.Error);
            if (!string.IsNullOrEmpty(s.Timing)) { StringField(sb, ref first, "timing", s.Timing); }

            Field(sb, ref first, "devices");
            sb.Append('[');
            for (int i = 0; i < s.Devices.Count; i++)
            {
                if (i > 0) { sb.Append(','); }
                AppendDevice(sb, s.Devices[i]);
            }
            sb.Append(']');
            sb.Append('}');
            return sb.ToString();
        }

        private static void AppendDevice(StringBuilder sb, Device d)
        {
            sb.Append('{');
            bool first = true;
            StringField(sb, ref first, "source", d.Source);
            NullableIntField(sb, ref first, "slot", d.Slot);
            BoolField(sb, ref first, "connected", d.Connected);
            StringField(sb, ref first, "productName", d.ProductName);
            StringField(sb, ref first, "deviceCode", d.DeviceCode);
            StringField(sb, ref first, "uid", d.Uid);
            NullableIntField(sb, ref first, "category", d.Category);
            NullableIntField(sb, ref first, "connectType", d.ConnectType);
            NullableIntField(sb, ref first, "level", d.Level);
            IntField(sb, ref first, "levelMax", d.LevelMax);
            StringField(sb, ref first, "levelText", d.LevelText);
            NullableIntField(sb, ref first, "percent", d.Percent);
            StringField(sb, ref first, "approx", d.Approx);
            StringField(sb, ref first, "connect", d.Connect);
            StringField(sb, ref first, "charging", d.Charging);
            BoolField(sb, ref first, "plugged", d.Plugged);
            sb.Append('}');
        }

        private static void Field(StringBuilder sb, ref bool first, string name)
        {
            if (!first) { sb.Append(','); }
            first = false;
            sb.Append('"').Append(name).Append("\":");
        }

        private static void IntField(StringBuilder sb, ref bool first, string name, int value)
        {
            Field(sb, ref first, name);
            sb.Append(value.ToString(CultureInfo.InvariantCulture));
        }

        private static void NullableIntField(StringBuilder sb, ref bool first, string name, int value)
        {
            Field(sb, ref first, name);
            sb.Append(value >= 0 ? value.ToString(CultureInfo.InvariantCulture) : "null");
        }

        private static void BoolField(StringBuilder sb, ref bool first, string name, bool value)
        {
            Field(sb, ref first, name);
            sb.Append(value ? "true" : "false");
        }

        private static void StringField(StringBuilder sb, ref bool first, string name, string value)
        {
            Field(sb, ref first, name);
            if (value == null) { sb.Append("null"); return; }
            Escape(sb, value);
        }

        public static void Escape(StringBuilder sb, string value)
        {
            sb.Append('"');
            foreach (char c in value)
            {
                switch (c)
                {
                    case '"': sb.Append("\\\""); break;
                    case '\\': sb.Append("\\\\"); break;
                    case '\b': sb.Append("\\b"); break;
                    case '\f': sb.Append("\\f"); break;
                    case '\n': sb.Append("\\n"); break;
                    case '\r': sb.Append("\\r"); break;
                    case '\t': sb.Append("\\t"); break;
                    default:
                        if (c < ' ') { sb.Append("\\u").Append(((int)c).ToString("x4")); }
                        else { sb.Append(c); }
                        break;
                }
            }
            sb.Append('"');
        }
    }
}
