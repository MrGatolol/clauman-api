# =============================================================
# cambiar_password.ps1
# Script para cambiar la contraseña de cualquier usuario del sistema
# directamente en la base de datos.
#
# Uso:
#   .\cambiar_password.ps1 -Username admin -NuevaPass "MiContrasenaSegura!"
#
# También invalida todas las sesiones activas del usuario para
# forzar re-login (por si la contraseña fue comprometida).
# =============================================================

param(
    [Parameter(Mandatory=$true)]
    [string]$Username,

    [Parameter(Mandatory=$true)]
    [string]$NuevaPass,

    [string]$Servidor = ".\SQLEXPRESS",
    [string]$BaseDatos = "ClaumanDB"
)

if ($NuevaPass.Length -lt 6) {
    Write-Host "ERROR: la contraseña debe tener al menos 6 caracteres." -ForegroundColor Red
    exit 1
}

# Hash SHA256 — debe coincidir con el algoritmo del backend
function HashSHA256($texto) {
    $sha = [System.Security.Cryptography.SHA256]::Create()
    $bytes = [System.Text.Encoding]::UTF8.GetBytes($texto)
    return (($sha.ComputeHash($bytes) | ForEach-Object { $_.ToString("x2") }) -join '')
}

$hash = HashSHA256 $NuevaPass

# Verificar que el usuario exista
$existe = sqlcmd -S $Servidor -d $BaseDatos -E -I -h -1 -W -Q "SET QUOTED_IDENTIFIER ON; SELECT COUNT(*) FROM Usuarios WHERE Username = '$Username';" |
    Select-String -Pattern '^\d+$' | ForEach-Object { [int]$_.Matches[0].Value }

if ($existe -eq 0) {
    Write-Host "ERROR: el usuario '$Username' no existe." -ForegroundColor Red
    exit 1
}

# Actualizar password
sqlcmd -S $Servidor -d $BaseDatos -E -I -Q "SET QUOTED_IDENTIFIER ON; UPDATE Usuarios SET PasswordHash = '$hash' WHERE Username = '$Username';" | Out-Null

# Invalidar sesiones activas del usuario
sqlcmd -S $Servidor -d $BaseDatos -E -I -Q "SET QUOTED_IDENTIFIER ON; DELETE FROM SesionTokens WHERE UsuarioId IN (SELECT Id FROM Usuarios WHERE Username = '$Username');" | Out-Null

Write-Host ""
Write-Host "Contrasena actualizada para '$Username'" -ForegroundColor Green
Write-Host "Sesiones activas invalidadas - el usuario debe volver a iniciar sesion." -ForegroundColor Yellow
