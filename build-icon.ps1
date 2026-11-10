param([string]$Preview)

$ErrorActionPreference = 'Stop'
Add-Type -AssemblyName System.Drawing.Common

function Rounded-Path([float]$X, [float]$Y, [float]$Width, [float]$Height, [float]$Radius) {
    $path = [Drawing.Drawing2D.GraphicsPath]::new()
    $diameter = 2 * $Radius
    $path.AddArc($X, $Y, $diameter, $diameter, 180, 90)
    $path.AddArc($X + $Width - $diameter, $Y, $diameter, $diameter, 270, 90)
    $path.AddArc($X + $Width - $diameter, $Y + $Height - $diameter, $diameter, $diameter, 0, 90)
    $path.AddArc($X, $Y + $Height - $diameter, $diameter, $diameter, 90, 90)
    $path.CloseFigure()
    $path
}

function Color([string]$Hex) { [Drawing.ColorTranslator]::FromHtml($Hex) }

function Render-Icon([int]$Size) {
    $bitmap = [Drawing.Bitmap]::new($Size, $Size, [Drawing.Imaging.PixelFormat]::Format32bppArgb)
    $canvas = [Drawing.Graphics]::FromImage($bitmap)
    $stream = [IO.MemoryStream]::new()
    try {
        $canvas.SmoothingMode = [Drawing.Drawing2D.SmoothingMode]::AntiAlias
        $canvas.PixelOffsetMode = [Drawing.Drawing2D.PixelOffsetMode]::HighQuality
        $canvas.ScaleTransform($Size / 256.0, $Size / 256.0)
        $tile = Rounded-Path 8 8 240 240 58
        $edge = [Drawing.Pen]::new((Color '#47819B'), 3)
        $background = [Drawing.Drawing2D.LinearGradientBrush]::new(
            [Drawing.RectangleF]::new(8, 8, 240, 240), (Color '#132747'), (Color '#116078'), [float]125)
        $shadow = Rounded-Path 43 132 170 65 20
        $shadowPaint = [Drawing.SolidBrush]::new([Drawing.Color]::FromArgb(62, 0, 10, 25))
        $keyBase = Rounded-Path 43 122 170 65 20
        $keyBasePaint = [Drawing.SolidBrush]::new((Color '#A3CCDC'))
        $keyFace = Rounded-Path 43 115 170 62 19
        $keyPaint = [Drawing.Drawing2D.LinearGradientBrush]::new(
            [Drawing.RectangleF]::new(43, 115, 170, 62), (Color '#FFFFFF'), (Color '#D7F0F5'), [float]90)
        $arrow = [Drawing.Drawing2D.GraphicsPath]::new()
        $arrow.AddBezier(61, 91, 68, 66, 89, 66, 115, 66)
        $arrow.AddLine(115, 66, 189, 66)
        $arrowHead = [Drawing.PointF[]]@(
            [Drawing.PointF]::new(171, 48),
            [Drawing.PointF]::new(190, 66),
            [Drawing.PointF]::new(171, 84))
        $arrowPen = [Drawing.Pen]::new((Color '#68F0C3'), 14)
        $arrowPen.StartCap = $arrowPen.EndCap = [Drawing.Drawing2D.LineCap]::Round
        $arrowPen.LineJoin = [Drawing.Drawing2D.LineJoin]::Round
        $spaceMark = [Drawing.PointF[]]@(
            [Drawing.PointF]::new(98, 143),
            [Drawing.PointF]::new(98, 153),
            [Drawing.PointF]::new(158, 153),
            [Drawing.PointF]::new(158, 143))
        $markPen = [Drawing.Pen]::new((Color '#277188'), 8)
        $markPen.StartCap = $markPen.EndCap = [Drawing.Drawing2D.LineCap]::Round
        $markPen.LineJoin = [Drawing.Drawing2D.LineJoin]::Round
        try {
            $canvas.FillPath($background, $tile)
            $canvas.DrawPath($edge, $tile)
            $canvas.DrawPath($arrowPen, $arrow)
            $canvas.DrawLines($arrowPen, $arrowHead)
            $canvas.FillPath($shadowPaint, $shadow)
            $canvas.FillPath($keyBasePaint, $keyBase)
            $canvas.FillPath($keyPaint, $keyFace)
            $canvas.DrawLines($markPen, $spaceMark)
            $bitmap.Save($stream, [Drawing.Imaging.ImageFormat]::Png)
            return ,$stream.ToArray()
        } finally {
            foreach ($object in @($tile,$edge,$background,$shadow,$shadowPaint,$keyBase,$keyBasePaint,
                    $keyFace,$keyPaint,$arrow,$arrowPen,$markPen)) { $object.Dispose() }
        }
    } finally {
        $stream.Dispose()
        $canvas.Dispose()
        $bitmap.Dispose()
    }
}

$sizes = @(16, 20, 24, 32, 40, 48, 64, 96, 128, 256)
$frames = @{}
foreach ($size in $sizes) { $frames[$size] = [byte[]](Render-Icon $size) }
$target = Join-Path $PSScriptRoot 'app.ico'
$writer = [IO.BinaryWriter]::new([IO.File]::Create($target))
try {
    $writer.Write([uint16]0)
    $writer.Write([uint16]1)
    $writer.Write([uint16]$sizes.Count)
    $offset = 6 + 16 * $sizes.Count
    foreach ($size in $sizes) {
        $dimension = [byte]($size % 256)
        $writer.Write($dimension)
        $writer.Write($dimension)
        $writer.Write([byte]0)
        $writer.Write([byte]0)
        $writer.Write([uint16]1)
        $writer.Write([uint16]32)
        $writer.Write([uint32]$frames[$size].Length)
        $writer.Write([uint32]$offset)
        $offset += $frames[$size].Length
    }
    foreach ($size in $sizes) { $writer.Write([byte[]]$frames[$size]) }
} finally { $writer.Dispose() }

[IO.File]::WriteAllBytes((Join-Path $PSScriptRoot 'app-logo.png'), $frames[256])
if ($Preview) { [IO.File]::WriteAllBytes($Preview, $frames[256]) }
