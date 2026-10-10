# Ícone Qrztweaks

Um "Q" geométrico cuja perna é um raio (desempenho), violeta sobre um bloco grafite, nas cores do app
(#7C3AED, #A78BFA, #F5F3FF sobre #1C1A25 → #101016).

A fonte é o desenho em vetor dentro de `tools/Create-AppIcon.ps1`. O script desenha cada tamanho direto na
resolução final, em vez de reduzir uma imagem grande:

- **16 a 32 px**: anel mais grosso, raio maior, sem borda nem brilho, para continuar nítido na barra de tarefas.
- **48 px ou mais**: brilho violeta e fio de borda, sempre por dentro do bloco (fora dele o fundo é transparente).
- O vão entre o anel e o raio é recortado do anel, então só aparece onde os dois se cruzam.

```powershell
powershell -File .\tools\Create-AppIcon.ps1      # app.png, app.ico, ícones do site e qrztweaks-icon.svg
powershell -File .\tools\Create-InstallerArt.ps1 # imagens do instalador, a partir do app.png
```

`qrztweaks-icon.svg` é gerado pelo script (versão grande) e serve para referência ou para abrir num editor.
