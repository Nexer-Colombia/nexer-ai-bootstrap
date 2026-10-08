BeforeAll {
    $script:manifestPath = Join-Path $PSScriptRoot '..\NexerAI\NexerAI.psd1'
}

Describe 'NexerAI module' {
    It 'has a valid manifest for Windows PowerShell 5.1' {
        $manifest = Test-ModuleManifest -Path $manifestPath -ErrorAction Stop
        $manifest.Version.ToString() | Should -Be '0.1.0'
        $manifest.PowerShellVersion.ToString() | Should -Be '5.1'
    }

    It 'exports exactly the public functions' {
        $module = Import-Module $manifestPath -Force -PassThru
        @($module.ExportedFunctions.Keys) | Sort-Object | Should -Be @('Assert-NexerPort', 'Get-NexerPortContract')
    }

    It 'uses only approved verbs and the Nexer noun prefix' {
        $module = Import-Module $manifestPath -Force -PassThru
        $approved = (Get-Verb).Verb
        foreach ($name in $module.ExportedFunctions.Keys) {
            $verb, $noun = $name -split '-', 2
            $approved | Should -Contain $verb
            $noun | Should -BeLike 'Nexer*'
        }
    }

    It 'keeps PowerShell sources ASCII only' {
        $root = Join-Path $PSScriptRoot '..'
        $files = Get-ChildItem -Path $root -Recurse -Include '*.ps1', '*.psm1', '*.psd1' -File
        foreach ($file in $files) {
            $bytes = [System.IO.File]::ReadAllBytes($file.FullName)
            @($bytes | Where-Object { $_ -gt 127 }).Count | Should -Be 0 -Because $file.Name
        }
    }
}
