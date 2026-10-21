# Nave

Nave é um navegador experimental para Windows, feito com Windows Forms e Microsoft Edge WebView2.

## Estado

Esta é uma versão de testes. Ainda não há instalador nem atualizador dentro do aplicativo. O GitHub Actions compila o app em pushes e pull requests; tags como `v0.1.0-beta.1` geram um pré-lançamento com ZIP portátil e checksum SHA-256.

## Requisitos

- Windows x64
- Microsoft Edge WebView2 Runtime (Evergreen)

## Compilar

Com o .NET 10 SDK instalado:

```powershell
dotnet publish NaveBrowser.csproj -c Release -r win-x64 --self-contained true -p:PublishSingleFile=true -p:IncludeNativeLibrariesForSelfExtract=true -o artifacts/publish
```

## Bloqueador

O bloqueio usa domínios do formato hosts, com uma lista integrada e uma lista mantida pelo projeto [StevenBlack/hosts](https://github.com/StevenBlack/hosts). O Nave atualiza a lista semanalmente, mantém uma cópia local para uso offline e permite atualizar manualmente ou liberar o site atual pelo menu de contexto do botão “Bloqueador”.

Esse método bloqueia requisições por domínio; não remove elementos visuais de anúncios nem bloqueia anúncios servidos pelo mesmo domínio do conteúdo. A lista também pode causar falsos positivos, então o Nave permite liberar um site individualmente. Isso não substitui proteção contra phishing ou malware. Os créditos e a licença da lista estão em [THIRD-PARTY-NOTICES.md](THIRD-PARTY-NOTICES.md).
