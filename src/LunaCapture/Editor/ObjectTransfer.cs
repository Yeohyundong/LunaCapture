using System.Text.Json;
using System.Text.Json.Serialization;
using LunaCapture.Interop;
using LunaCapture.Services;

namespace LunaCapture.Editor;

internal sealed class ObjectTransferData
{
    public int Schema { get; set; } = 1;
    public List<AnnotationDto> Items { get; set; } = [];
}

[JsonSerializable(typeof(ObjectTransferData))]
internal sealed partial class ObjectTransferJson : JsonSerializerContext;

internal static class ObjectTransfer
{
    public static byte[] Encode(IEnumerable<Annotation> items)
    {
        var data = new ObjectTransferData { Items = items.Select(AnnotationJson.ToDto).ToList() };
        if (data.Items.Count is 0 or > 10000) throw new InvalidDataException("복사할 객체 수가 잘못되었습니다.");
        byte[] bytes = JsonSerializer.SerializeToUtf8Bytes(data, ObjectTransferJson.Default.ObjectTransferData);
        if (bytes.Length > ClipboardService.MaxObjectBytes) throw new InvalidDataException("복사할 객체가 너무 큽니다.");
        return bytes;
    }

    public static List<Annotation> Decode(byte[] bytes)
    {
        if (bytes.Length > ClipboardService.MaxObjectBytes) throw new InvalidDataException("객체 데이터가 너무 큽니다.");
        var data = JsonSerializer.Deserialize(bytes, ObjectTransferJson.Default.ObjectTransferData);
        if (data == null || data.Schema != 1 || data.Items == null || data.Items.Count is 0 or > 10000)
            throw new InvalidDataException("객체 데이터 형식이 잘못되었습니다.");
        var items = new List<Annotation>();
        long imageBytes = 0;
        foreach (var dto in data.Items)
        {
            if (dto == null || dto.Pts == null || dto.Pts.Length > 2000000 ||
                dto.Pts.Length < 2 || dto.Pts.Length % 2 != 0 ||
                dto.Pts.Any(x => !float.IsFinite(x) || MathF.Abs(x) > 10000000) ||
                !float.IsFinite(dto.Width) || dto.Width is < 0 or > 100000 ||
                !float.IsFinite(dto.DetailScale) || dto.DetailScale <= 0 ||
                !float.IsFinite(dto.FontSize) || dto.FontSize is < 0 or > 100000 ||
                !float.IsFinite(dto.Size) || dto.Size is < 0 or > 100000)
                throw new InvalidDataException("객체 좌표가 잘못되었습니다.");
            if ((dto.Type is "arrow" or "shape" or "mosaic" or "image" or "patch" or "spotlight") && dto.Pts.Length < 4)
                throw new InvalidDataException("객체 끝점이 없습니다.");
            if (dto.Type == "number" && dto.Number < 1) throw new InvalidDataException("번호가 잘못되었습니다.");
            if (dto.Type == "image")
            {
                if (dto.Iw <= 0 || dto.Ih <= 0) throw new InvalidDataException("그림 크기가 잘못되었습니다.");
                long pixels = (long)dto.Iw * dto.Ih;
                if (pixels > 128 * 1024 * 1024 / 4) throw new InvalidDataException("그림 객체가 너무 큽니다.");
                imageBytes += pixels * 4;
                if (imageBytes > 128 * 1024 * 1024) throw new InvalidDataException("그림 객체가 너무 큽니다.");
            }
            items.Add(AnnotationJson.FromDto(dto) ?? throw new InvalidDataException("지원하지 않는 객체입니다."));
        }
        return items;
    }

    public static RectF Bounds(IEnumerable<Annotation> items, Gfx g)
    {
        var boxes = items.Select(x => x.Bounds(g)).ToArray();
        if (boxes.Length == 0) return default;
        float left = boxes.Min(x => x.X), top = boxes.Min(x => x.Y);
        float right = boxes.Max(x => x.X + x.Width), bottom = boxes.Max(x => x.Y + x.Height);
        return new(left, top, right - left, bottom - top);
    }

    public static bool Contains(RectF selection, RectF item) =>
        item.X >= selection.X && item.Y >= selection.Y &&
        item.X + item.Width <= selection.X + selection.Width && item.Y + item.Height <= selection.Y + selection.Height;

    public static void FitInto(IReadOnlyList<Annotation> items, int width, int height, Gfx g)
    {
        var bounds = Bounds(items, g);
        float x = bounds.Width > width ? 0 : Math.Clamp(bounds.X, 0, width - bounds.Width);
        float y = bounds.Height > height ? 0 : Math.Clamp(bounds.Y, 0, height - bounds.Height);
        foreach (var item in items) item.Offset(x - bounds.X, y - bounds.Y);
    }
}
