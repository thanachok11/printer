$imgPath = "C:\Users\admin\Downloads\w80-windows-build\W80PrintService\Receipt.png"   # เปลี่ยนเป็น path รูปของคุณ
$bytes = [System.IO.File]::ReadAllBytes($imgPath)
$b64 = [Convert]::ToBase64String($bytes)

$body = @{
  imageBase64 = "data:image/png;base64,$b64"
  paperWidth  = 576
  threshold   = 170
  cut         = $true
  saveDebug   = $true
  # printerName = "ชื่อ_printer_ใน_windows"  # จะใส่ override ก็ได้
} | ConvertTo-Json -Depth 6

Invoke-RestMethod -Uri "http://127.0.0.1:9109/print/image" -Method Post -ContentType "application/json" -Body $body
