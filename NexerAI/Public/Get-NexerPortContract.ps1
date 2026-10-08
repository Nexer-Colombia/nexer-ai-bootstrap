function Get-NexerPortContract {
    <#
    .SYNOPSIS
    Returns a copy of the port contracts (port name -> required method names).
    #>
    [CmdletBinding()]
    [OutputType([System.Collections.Specialized.OrderedDictionary])]
    param()

    $copy = [ordered]@{}
    foreach ($name in $script:NexerPortContracts.Keys) {
        $copy[$name] = [string[]] @($script:NexerPortContracts[$name])
    }
    $copy
}
