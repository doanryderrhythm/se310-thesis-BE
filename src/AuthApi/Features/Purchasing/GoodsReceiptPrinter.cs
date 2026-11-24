using System.Globalization;
using System.Text;
using System.Text.Encodings.Web;
using System.Text.Unicode;

namespace AuthApi.Features.Purchasing;

/// <summary>Renders a goods receipt as a self-contained A4 HTML page; the browser prints it or saves it as PDF.</summary>
internal static class GoodsReceiptPrinter
{
    private static readonly CultureInfo Vi = CultureInfo.InvariantCulture;

    // Keeps Vietnamese letters as they are (WebUtility.HtmlEncode turns á, à, ... into numeric entities).
    private static readonly HtmlEncoder Html = HtmlEncoder.Create(UnicodeRanges.All);

    private static string E(string? value) => Html.Encode(value ?? "");

    private static string Money(double value) =>
        Math.Round(value, 0).ToString("N0", Vi).Replace(',', '.');

    private static string Local(DateTime utc) => (utc + AuthApi.Common.VnTime.Offset).ToString("dd/MM/yyyy HH:mm", Vi);

    public static string Render(ReceiptDetail r, string storeName, string? storeAddress, string? storePhone)
    {
        var html = new StringBuilder();
        var banner = r.Status switch
        {
            ReceiptStatuses.Draft => "BẢN NHÁP — CHƯA NHẬP KHO",
            ReceiptStatuses.Cancelled => "ĐÃ HỦY",
            _ => null
        };

        html.Append($$"""
<!DOCTYPE html>
<html lang="vi">
<head>
<meta charset="utf-8">
<meta name="viewport" content="width=device-width, initial-scale=1">
<title>Phiếu nhập hàng {{E(r.ReceiptCode)}}</title>
<style>
  @page { size: A4; margin: 14mm; }
  * { box-sizing: border-box; }
  body { font-family: "Segoe UI", Arial, sans-serif; font-size: 13px; color: #111; margin: 0; padding: 16px; }
  .sheet { max-width: 190mm; margin: 0 auto; }
  header { display: flex; justify-content: space-between; gap: 16px; border-bottom: 2px solid #111; padding-bottom: 10px; }
  header h1 { font-size: 20px; margin: 0 0 4px; }
  header .meta { text-align: right; }
  h2 { text-align: center; font-size: 18px; letter-spacing: .04em; margin: 18px 0 2px; }
  .code { text-align: center; margin-bottom: 14px; }
  .banner { text-align: center; font-weight: 700; color: #b00020; border: 2px solid #b00020; padding: 4px; margin: 8px 0; }
  .table-wrap { max-width: 100%; overflow-x: auto; }
  table { width: 100%; border-collapse: collapse; margin-top: 12px; }
  td { overflow-wrap: anywhere; }
  th, td { border: 1px solid #444; padding: 5px 7px; vertical-align: top; }
  th { background: #f0f0f0; text-align: center; }
  td.num, th.num { text-align: right; white-space: nowrap; }
  td.c { text-align: center; }
  .info p { margin: 2px 0; }
  .total td { font-weight: 700; }
  .words { margin-top: 10px; font-style: italic; }
  .sign { display: flex; justify-content: space-around; margin-top: 34px; text-align: center; }
  .sign div { width: 30%; }
  .sign small { color: #555; }
  .actions { text-align: center; margin-bottom: 14px; }
  button { padding: 8px 18px; font-size: 14px; cursor: pointer; }
  @media (max-width: 600px) { header { flex-direction: column; gap: 6px; } header .meta { text-align: left; } table { min-width: 650px; } .sign { gap: 10px; font-size: 11px; } }
  @media print { .actions { display: none; } body { padding: 0; } .table-wrap { overflow: visible; } table { table-layout: fixed; min-width: 0; font-size: 11px; } th:nth-child(1) { width: 5%; } th:nth-child(2) { width: 18%; } th:nth-child(3) { width: 25%; } th:nth-child(4) { width: 13%; } th:nth-child(5) { width: 6%; } th:nth-child(6) { width: 15%; } th:nth-child(7) { width: 18%; } }
</style>
</head>
<body>
<div class="sheet">
<div class="actions"><button onclick="window.print()">In phiếu</button></div>
<header>
  <div><h1>{{E(storeName)}}</h1>
  {{(storeAddress is null ? "" : $"<div>{E(storeAddress)}</div>")}}
  {{(storePhone is null ? "" : $"<div>Điện thoại: {E(storePhone)}</div>")}}</div>
  <div class="meta"><div>Ngày nhập: <b>{{Local(r.ReceivedAt)}}</b></div></div>
</header>
<h2>PHIẾU NHẬP HÀNG</h2>
<div class="code">Số phiếu: <b>{{E(r.ReceiptCode)}}</b></div>
{{(banner is null ? "" : $"<div class=\"banner\">{banner}</div>")}}
<div class="info">
  <p>Nhà cung cấp: <b>{{E(r.Supplier?.Name)}}</b></p>
  {{(string.IsNullOrEmpty(r.Supplier?.Address) ? "" : $"<p>Địa chỉ: {E(r.Supplier!.Address)}</p>")}}
  {{(string.IsNullOrEmpty(r.Supplier?.Phone) ? "" : $"<p>Điện thoại: {E(r.Supplier!.Phone)}</p>")}}
  {{(string.IsNullOrEmpty(r.Supplier?.TaxCode) ? "" : $"<p>Mã số thuế: {E(r.Supplier!.TaxCode)}</p>")}}
  {{(string.IsNullOrEmpty(r.SupplierInvoiceNo) ? "" : $"<p>Số hóa đơn của nhà cung cấp: <b>{E(r.SupplierInvoiceNo)}</b></p>")}}
  {{(string.IsNullOrEmpty(r.Note) ? "" : $"<p>Ghi chú: {E(r.Note)}</p>")}}
</div>
<div class="table-wrap"><table>
<thead><tr><th>STT</th><th>Mã SKU</th><th>Sản phẩm</th><th>Size / Màu</th><th class="num">SL</th><th class="num">Đơn giá (đ)</th><th class="num">Thành tiền (đ)</th></tr></thead>
<tbody>
""");

        foreach (var (item, index) in r.Items.Select((x, n) => (x, n)))
        {
            html.Append($"<tr><td class=\"c\">{index + 1}</td><td>{E(item.Sku)}</td><td>{E(item.ProductName)}</td>"
                + $"<td>{E(item.Size)} / {E(item.Color)}</td><td class=\"num\">{item.Quantity}</td>"
                + $"<td class=\"num\">{Money(item.UnitCost)}</td><td class=\"num\">{Money(item.LineTotal)}</td></tr>\n");
        }

        html.Append($$"""
<tr class="total"><td colspan="4" style="text-align:right">Tổng cộng</td><td class="num">{{r.TotalQuantity}}</td><td></td><td class="num">{{Money(r.TotalAmount)}}</td></tr>
</tbody>
</table></div>
<p class="words">Tổng tiền bằng chữ: {{E(VietnameseNumber.Money((long)Math.Round(r.TotalAmount)))}}</p>
<div class="sign">
  <div><b>Người lập phiếu</b><br><small>(Ký, ghi rõ họ tên)</small><br><br><br>{{E(r.CreatedBy)}}</div>
  <div><b>Người giao hàng</b><br><small>(Ký, ghi rõ họ tên)</small></div>
  <div><b>Thủ kho</b><br><small>(Ký, ghi rõ họ tên)</small><br><br><br>{{E(r.CompletedBy)}}</div>
</div>
</div>
</body>
</html>
""");
        return html.ToString();
    }
}

/// <summary>Reads a whole number of đồng in Vietnamese ("một triệu hai trăm nghìn đồng chẵn").</summary>
internal static class VietnameseNumber
{
    private static readonly string[] Digits = ["không", "một", "hai", "ba", "bốn", "năm", "sáu", "bảy", "tám", "chín"];
    private static readonly string[] Scales = ["", "nghìn", "triệu", "tỷ", "nghìn tỷ", "triệu tỷ"];

    public static string Money(long amount)
    {
        var words = Words(amount);
        return char.ToUpper(words[0], CultureInfo.InvariantCulture) + words[1..] + " đồng chẵn.";
    }

    public static string Words(long number)
    {
        if (number == 0) return "không";
        if (number < 0) return "âm " + Words(-number);

        var groups = new List<int>();
        for (var n = number; n > 0; n /= 1000) groups.Add((int)(n % 1000));

        var parts = new List<string>();
        for (var i = groups.Count - 1; i >= 0; i--)
        {
            if (groups[i] == 0) continue;
            // A group below 100 that follows a higher group reads its empty hundreds ("không trăm lẻ năm").
            var readHundreds = i < groups.Count - 1;
            var text = Group(groups[i], readHundreds);
            parts.Add(Scales[i].Length == 0 ? text : $"{text} {Scales[i]}");
        }

        return string.Join(" ", parts);
    }

    private static string Group(int value, bool readHundreds)
    {
        var hundreds = value / 100;
        var tens = value / 10 % 10;
        var units = value % 10;
        var text = new List<string>();
        if (hundreds > 0 || readHundreds) text.Add($"{Digits[hundreds]} trăm");
        if (tens > 1)
        {
            text.Add($"{Digits[tens]} mươi");
            if (units == 1) text.Add("mốt");
            else if (units == 4) text.Add("tư");
            else if (units == 5) text.Add("lăm");
            else if (units > 0) text.Add(Digits[units]);
        }
        else if (tens == 1)
        {
            text.Add("mười");
            if (units == 5) text.Add("lăm");
            else if (units > 0) text.Add(Digits[units]);
        }
        else if (units > 0)
        {
            if (hundreds > 0 || readHundreds) text.Add("lẻ");
            text.Add(Digits[units]);
        }

        return string.Join(" ", text);
    }
}
