BeforeAll {
    Import-Module (Join-Path $PSScriptRoot '..\NexerAI\NexerAI.psd1') -Force
    . (Join-Path $PSScriptRoot 'Fakes\Fakes.ps1')
}

Describe 'New-NexerFake' {
    It 'creates a <_> fake that satisfies its port contract' -ForEach @(
        'ProfileSource', 'StackDetector', 'Prompter', 'AgentInstaller', 'CredentialStore', 'IssueReporter'
    ) {
        $fake = New-NexerFake -Port $_
        { Assert-NexerPort -Port $fake -Name $_ } | Should -Not -Throw
    }

    It 'records each call with its method name and arguments' {
        $fake = New-NexerFake -Port 'AgentInstaller'
        $fake.InstallPlugin('nexer-core', 'user', @{ a = 1 })
        $fake.GetInstalledPlugins()

        $fake.Calls.Count | Should -Be 2
        $fake.Calls[0].Method | Should -Be 'InstallPlugin'
        $fake.Calls[0].Arguments[0] | Should -Be 'nexer-core'
        $fake.Calls[0].Arguments[1] | Should -Be 'user'
        $fake.Calls[0].Arguments[2].a | Should -Be 1
        $fake.Calls[1].Method | Should -Be 'GetInstalledPlugins'
        $fake.Calls[1].Arguments.Count | Should -Be 0
    }

    It 'returns a configured canned value' {
        $fake = New-NexerFake -Port 'IssueReporter' -Returns @{ CreateIssue = 'https://example.test/issues/1' }
        $fake.CreateIssue('title', 'body', @('project-profile')) | Should -Be 'https://example.test/issues/1'
    }

    It 'invokes a configured scriptblock with the call arguments' {
        $fake = New-NexerFake -Port 'CredentialStore' -Returns @{ Test = { param($url) $url -eq 'https://dev.azure.com/acme' } }
        $fake.Test('https://dev.azure.com/acme') | Should -BeTrue
        $fake.Test('https://other.test') | Should -BeFalse
    }

    It 'returns null for a method without a configured value' {
        $fake = New-NexerFake -Port 'StackDetector'
        $fake.Detect('C:\repo') | Should -BeNullOrEmpty
    }

    It 'rejects a configured value for a method outside the contract' {
        { New-NexerFake -Port 'StackDetector' -Returns @{ Detekt = @() } } | Should -Throw "*Detekt*"
    }

    It 'rejects an unknown port' {
        { New-NexerFake -Port 'Nope' } | Should -Throw "*Unknown port 'Nope'*"
    }
}
