# Eve Grid Scout Client App

WPF application to use Sanderling Memory Reading on an Eve Client window, identify a specific in-game overview and submit the content to the Eve Grid Scout web service for logging.

The GridScout2 project is original work and an evolution of GridScout, which used TesseractOCR to read the contents of the overview from screencaptures.

## Acknoledgements

The read-memory-64-bit project is a direct fork from [Arcitectus/Sanderling on Github](https://github.com/Arcitectus/Sanderling/tree/main/implement/read-memory-64-bit) with some minor refactoring and extra bits.

eve-parse-ui is a C# port of the ParseUserInterface.elm file from Arcitectus' alternate-ui Elm project, here: https://github.com/Arcitectus/Sanderling/blob/main/implement/alternate-ui/source/src/EveOnline/ParseUserInterface.elm
Many parsers for windows and elements from the Eve Game Client have been ported over.
Many parsers in the Elm project have not yet been ported over.
Some parsers for game client elements that did not exist in the Elm project have been developed.
