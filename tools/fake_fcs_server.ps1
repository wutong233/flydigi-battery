param(
    [string]$Name = 'fcs_test.sock',
    [string]$Hex,
    [ValidateSet('small', 'chunk', 'gzip')]
    [string]$Mode = 'small',
    [int]$TimeoutMs = 8000
)

$resp = New-Object byte[] ($Hex.Length / 2)
for ($i = 0; $i -lt $Hex.Length; $i += 2) {
    $resp[$i / 2] = [Convert]::ToByte($Hex.Substring($i, 2), 16)
}

function ReadExact([System.IO.Stream]$s, [int]$n) {
    $buf = New-Object byte[] $n
    $off = 0
    while ($off -lt $n) {
        $r = $s.Read($buf, $off, $n - $off)
        if ($r -le 0) { throw 'client closed' }
        $off += $r
    }
    return $buf
}

$pipe = New-Object System.IO.Pipes.NamedPipeServerStream(
    $Name,
    [System.IO.Pipes.PipeDirection]::InOut,
    1,
    [System.IO.Pipes.PipeTransmissionMode]::Byte,
    [System.IO.Pipes.PipeOptions]::None)
$pipe.WaitForConnection()

$hdr = ''
while ($true) {
    $b = $pipe.ReadByte()
    if ($b -lt 0 -or $b -eq 10) { break }
    $hdr += [char]$b
}
$len = [BitConverter]::ToInt32((ReadExact $pipe 4), 0)
$req = ReadExact $pipe $len
Write-Host ("[server] header='{0}' len={1} payload={2}" -f $hdr, $len, ([BitConverter]::ToString($req) -replace '-', ' '))

$head = [Text.Encoding]::ASCII.GetBytes("FDG_PROTOCOL`n")
switch ($Mode) {
    'small' {
        $pipe.Write($head, 0, $head.Length)
        $l = [BitConverter]::GetBytes([int32]$resp.Length)
        $pipe.Write($l, 0, 4)
        $pipe.Write($resp, 0, $resp.Length)
    }
    'chunk' {
        $ch = [Text.Encoding]::ASCII.GetBytes("FDG_CHUNK`n")
        $pipe.Write($ch, 0, $ch.Length)
        $tot = [BitConverter]::GetBytes([int32]$resp.Length)
        $pipe.Write($tot, 0, 4)
        $half = [int]($resp.Length / 2)
        $rest = $resp.Length - $half
        $n = [BitConverter]::GetBytes([int32]2)
        $pipe.Write($n, 0, 4)
        $offs = 0
        foreach ($cl in @($half, $rest)) {
            $pl = [BitConverter]::GetBytes([int32]$cl)
            $pipe.Write($pl, 0, 4)
            $pipe.Write($resp, $offs, $cl)
            $offs += $cl
        }
    }
    'gzip' {
        $ms = New-Object System.IO.MemoryStream
        $gz = New-Object System.IO.Compression.GZipStream($ms, [System.IO.Compression.CompressionMode]::Compress)
        $gz.Write($resp, 0, $resp.Length)
        $gz.Dispose()
        $blob = $ms.ToArray()
        $ch = [Text.Encoding]::ASCII.GetBytes("FDG_COMPRESSED`n")
        $pipe.Write($ch, 0, $ch.Length)
        $l = [BitConverter]::GetBytes([int32]$blob.Length)
        $pipe.Write($l, 0, 4)
        $pipe.Write($blob, 0, $blob.Length)
    }
}
$pipe.Flush()
$pipe.WaitForPipeDrain()
$pipe.Dispose()
Write-Host '[server] done'
