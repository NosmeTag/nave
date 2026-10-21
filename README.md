# Nave

Nave é um navegador experimental para Windows, feito com Windows Forms e Microsoft Edge WebView2.

## Estado

Esta é uma versão de testes e ainda não há instalador. O Nave verifica o GitHub uma vez por dia e, quando há uma versão publicada mais nova, pede confirmação antes de baixar e instalar. O pacote e seu checksum SHA-256 são conferidos antes da substituição do executável. O GitHub Actions compila em pushes e pull requests; tags como `v0.1.1-beta.1` geram um pré-lançamento com ZIP portátil e checksum.

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

## Extensões

O Nave permite instalar extensões WebView2 a partir de uma pasta local descompactada que contenha `manifest.json`. Use **Extensões** na barra lateral para instalar, ativar, pausar ou remover uma extensão. A instalação mostra o nome e as permissões declaradas pelo pacote; instale apenas extensões obtidas de uma fonte confiável. Por enquanto, o Nave não tem uma loja integrada nem atualiza extensões automaticamente. O suporte do WebView2 não é compatível com todas as extensões de Edge ou Chrome.
