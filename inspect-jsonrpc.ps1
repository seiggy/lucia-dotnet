$asm = [System.Reflection.Assembly]::LoadFrom('d:/github/lucia-dotnet/lucia.Tests/bin/Debug/net10.0/A2A.dll')

function Show-Properties($typeName) {
    $type = $asm.GetType($typeName, $false)
    if (-not $type) {
        "Type not found: $typeName" | Write-Output
        return
    }

            Write-Output "Properties for ${typeName}:"
    foreach ($p in $type.GetProperties()) {
            Write-Output " - $($p.Name): $($p.PropertyType.FullName)"
    }
        Write-Output ""
}

Show-Properties 'A2A.JsonRpcRequest'
Show-Properties 'A2A.JsonRpcResponse'
Show-Properties 'A2A.AgentMessage'
Show-Properties 'A2A.TextPart'
Show-Properties 'A2A.MessageSendParams'
Show-Properties 'A2A.JsonRpcId'
