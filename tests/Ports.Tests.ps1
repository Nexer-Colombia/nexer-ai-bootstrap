BeforeAll {
    Import-Module (Join-Path $PSScriptRoot '..\NexerAI\NexerAI.psd1') -Force
}

Describe 'Get-NexerPortContract' {
    It 'defines exactly the six ports' {
        $names = @((Get-NexerPortContract).Keys) | Sort-Object
        $names | Should -Be @('AgentInstaller', 'CredentialStore', 'IssueReporter', 'ProfileSource', 'Prompter', 'StackDetector')
    }

    It 'defines the methods of <Port>' -ForEach @(
        @{ Port = 'ProfileSource'; Methods = @('GetProfiles', 'GetPluginNames') }
        @{ Port = 'StackDetector'; Methods = @('Detect') }
        @{ Port = 'Prompter'; Methods = @('Choose', 'ReadSecret', 'Confirm') }
        @{ Port = 'AgentInstaller'; Methods = @('GetInstalledPlugins', 'InstallPlugin', 'AddMcpServer') }
        @{ Port = 'CredentialStore'; Methods = @('Test', 'Get', 'Set') }
        @{ Port = 'IssueReporter'; Methods = @('CreateIssue') }
    ) {
        (Get-NexerPortContract)[$Port] | Should -Be $Methods
    }

    It 'returns a copy that callers cannot use to change the contract' {
        $copy = Get-NexerPortContract
        $copy['StackDetector'] = @('Other')
        $copy.Remove('Prompter')
        (Get-NexerPortContract)['StackDetector'] | Should -Be @('Detect')
        (Get-NexerPortContract).Contains('Prompter') | Should -BeTrue
    }
}

Describe 'Assert-NexerPort' {
    BeforeAll {
        function New-TestObject([string[]] $Methods) {
            $obj = [pscustomobject]@{}
            foreach ($m in $Methods) {
                $obj | Add-Member -MemberType ScriptMethod -Name $m -Value { $null }
            }
            $obj
        }
    }

    It 'passes when every contract method exists' {
        $obj = New-TestObject @('Detect')
        { Assert-NexerPort -Port $obj -Name 'StackDetector' } | Should -Not -Throw
    }

    It 'passes when the object has extra members' {
        $obj = New-TestObject @('CreateIssue', 'Extra')
        { Assert-NexerPort -Port $obj -Name 'IssueReporter' } | Should -Not -Throw
    }

    It 'throws naming every missing method' {
        $obj = New-TestObject @('Choose')
        { Assert-NexerPort -Port $obj -Name 'Prompter' } |
            Should -Throw "*'Prompter'*missing method(s): ReadSecret, Confirm*"
    }

    It 'does not accept a property in place of a method' {
        $obj = [pscustomobject]@{ Detect = 'not a method' }
        { Assert-NexerPort -Port $obj -Name 'StackDetector' } | Should -Throw '*Detect*'
    }

    It 'throws for an unknown port name and lists the known ones' {
        $obj = New-TestObject @('Detect')
        { Assert-NexerPort -Port $obj -Name 'Nope' } | Should -Throw "*Unknown port 'Nope'*ProfileSource*"
    }

    It 'throws for a null port' {
        { Assert-NexerPort -Port $null -Name 'StackDetector' } | Should -Throw
    }
}
