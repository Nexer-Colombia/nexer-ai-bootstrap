# In-memory fakes for the NexerAI ports. Test-only; not shipped in the module.
# Requires the NexerAI module to be imported first.

function New-NexerFake {
    param(
        [Parameter(Mandatory = $true)]
        [string] $Port,

        # Method name -> canned value, or a scriptblock invoked with the call arguments.
        [hashtable] $Returns = @{}
    )

    $contract = Get-NexerPortContract
    if (-not $contract.Contains($Port)) {
        throw "Unknown port '$Port'. Known ports: $(@($contract.Keys) -join ', ')."
    }
    foreach ($key in $Returns.Keys) {
        if ($contract[$Port] -notcontains $key) {
            throw "Port '$Port' has no method '$key'."
        }
    }

    $fake = [pscustomobject]@{
        PortName = $Port
        Calls    = New-Object System.Collections.Generic.List[object]
        Returns  = $Returns
    }
    $fake | Add-Member -MemberType ScriptMethod -Name Respond -Value {
        param([string] $Method, [object[]] $Arguments)
        $this.Calls.Add([pscustomobject]@{ Method = $Method; Arguments = $Arguments })
        $value = $this.Returns[$Method]
        if ($value -is [scriptblock]) { & $value @Arguments } else { $value }
    }
    foreach ($method in $contract[$Port]) {
        $body = '$this.Respond(''{0}'', [object[]] $args)' -f $method
        $fake | Add-Member -MemberType ScriptMethod -Name $method -Value ([scriptblock]::Create($body))
    }
    $fake
}
