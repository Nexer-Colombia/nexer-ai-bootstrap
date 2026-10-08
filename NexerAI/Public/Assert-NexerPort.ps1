function Assert-NexerPort {
    <#
    .SYNOPSIS
    Throws unless the object implements every method of the named port.
    #>
    [CmdletBinding()]
    param(
        [Parameter(Mandatory = $true)]
        [object] $Port,

        [Parameter(Mandatory = $true)]
        [string] $Name
    )

    if (-not $script:NexerPortContracts.Contains($Name)) {
        $known = @($script:NexerPortContracts.Keys) -join ', '
        throw "Unknown port '$Name'. Known ports: $known."
    }

    $present = @($Port.PSObject.Methods | ForEach-Object { $_.Name })
    $missing = @($script:NexerPortContracts[$Name] | Where-Object { $present -notcontains $_ })
    if ($missing.Count -gt 0) {
        throw "Object does not implement port '$Name': missing method(s): $($missing -join ', ')."
    }
}
