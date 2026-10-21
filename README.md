# Nave

Nave é um navegador experimental para Windows, feito com Windows Forms e Microsoft Edge WebView2.

## Estado

Esta é uma versão de testes. Ainda não há instalador nem atualização automática dentro do aplicativo. As versões de teste serão publicadas como pré-lançamentos no GitHub.

## Requisitos

- Windows x64
- Microsoft Edge WebView2 Runtime (Evergreen)

## Compilar

Com o .NET 10 SDK instalado:

```powershell
dotnet publish NaveBrowser.csproj -c Release -r win-x64 --self-contained true -p:PublishSingleFile=true -p:IncludeNativeLibrariesForSelfExtract=true -o artifacts/publish
```

O bloqueador de anúncios ainda é básico e usa uma lista local de domínios. Ele não substitui proteção contra phishing ou malware.
