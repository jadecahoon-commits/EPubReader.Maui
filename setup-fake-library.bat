@echo off
REM Create a fake Calibre Library structure locally, then push to emulator
REM Run this from a terminal with adb on your PATH

set LOCAL=fake-calibre-library
set REMOTE=/sdcard/Download/CalibreLibrary

REM Clean up any previous run
if exist %LOCAL% rmdir /s /q %LOCAL%

REM ── Author 1: Jane Austen ──
mkdir "%LOCAL%\Jane Austen\Pride and Prejudice"
mkdir "%LOCAL%\Jane Austen\Sense and Sensibility"

REM Create a minimal OPF for Pride and Prejudice
(
echo ^<?xml version="1.0" encoding="utf-8"?^>
echo ^<package^>
echo   ^<metadata^>
echo     ^<dc:title^>Pride and Prejudice^</dc:title^>
echo     ^<dc:description^>A classic novel about the Bennet family and Mr. Darcy.^</dc:description^>
echo     ^<meta name="calibre:series_index" content="1"/^>
echo   ^</metadata^>
echo ^</package^>
) > "%LOCAL%\Jane Austen\Pride and Prejudice\metadata.opf"

REM Create a minimal EPUB (just a zip with mimetype)
mkdir "%LOCAL%\Jane Austen\Pride and Prejudice\temp_epub"
echo application/epub+zip> "%LOCAL%\Jane Austen\Pride and Prejudice\temp_epub\mimetype"
mkdir "%LOCAL%\Jane Austen\Pride and Prejudice\temp_epub\OEBPS"
(
echo ^<?xml version="1.0" encoding="utf-8"?^>
echo ^<html xmlns="http://www.w3.org/1999/xhtml"^>
echo ^<head^>^<title^>Chapter 1^</title^>^</head^>
echo ^<body^>
echo ^<h1^>Chapter 1^</h1^>
echo ^<p^>It is a truth universally acknowledged, that a single man in possession of a good fortune, must be in want of a wife.^</p^>
echo ^<p^>However little known the feelings or views of such a man may be on his first entering a neighbourhood, this truth is so well fixed in the minds of the surrounding families, that he is considered as the rightful property of some one or other of their daughters.^</p^>
echo ^</body^>
echo ^</html^>
) > "%LOCAL%\Jane Austen\Pride and Prejudice\temp_epub\OEBPS\chapter1.xhtml"
(
echo ^<?xml version="1.0" encoding="utf-8"?^>
echo ^<html xmlns="http://www.w3.org/1999/xhtml"^>
echo ^<head^>^<title^>Chapter 2^</title^>^</head^>
echo ^<body^>
echo ^<h1^>Chapter 2^</h1^>
echo ^<p^>Mr. Bennet was among the earliest of those who waited on Mr. Bingley. He had always intended to visit him, though to the last always assuring his wife that he should not go.^</p^>
echo ^</body^>
echo ^</html^>
) > "%LOCAL%\Jane Austen\Pride and Prejudice\temp_epub\OEBPS\chapter2.xhtml"
(
echo ^<?xml version="1.0" encoding="utf-8"?^>
echo ^<package xmlns="http://www.idpf.org/2007/opf" version="3.0"^>
echo   ^<metadata xmlns:dc="http://purl.org/dc/elements/1.1/"^>
echo     ^<dc:title^>Pride and Prejudice^</dc:title^>
echo   ^</metadata^>
echo   ^<manifest^>
echo     ^<item id="ch1" href="chapter1.xhtml" media-type="application/xhtml+xml"/^>
echo     ^<item id="ch2" href="chapter2.xhtml" media-type="application/xhtml+xml"/^>
echo   ^</manifest^>
echo   ^<spine^>
echo     ^<itemref idref="ch1"/^>
echo     ^<itemref idref="ch2"/^>
echo   ^</spine^>
echo ^</package^>
) > "%LOCAL%\Jane Austen\Pride and Prejudice\temp_epub\OEBPS\content.opf"
(
echo ^<?xml version="1.0" encoding="utf-8"?^>
echo ^<container xmlns="urn:oasis:names:tc:opendocument:xmlns:container" version="1.0"^>
echo   ^<rootfiles^>
echo     ^<rootfile full-path="OEBPS/content.opf" media-type="application/oebps-package+xml"/^>
echo   ^</rootfiles^>
echo ^</container^>
) > "%LOCAL%\Jane Austen\Pride and Prejudice\temp_epub\META-INF\container.xml" 2>nul
mkdir "%LOCAL%\Jane Austen\Pride and Prejudice\temp_epub\META-INF"
(
echo ^<?xml version="1.0" encoding="utf-8"?^>
echo ^<container xmlns="urn:oasis:names:tc:opendocument:xmlns:container" version="1.0"^>
echo   ^<rootfiles^>
echo     ^<rootfile full-path="OEBPS/content.opf" media-type="application/oebps-package+xml"/^>
echo   ^</rootfiles^>
echo ^</container^>
) > "%LOCAL%\Jane Austen\Pride and Prejudice\temp_epub\META-INF\container.xml"

REM We can't easily zip from batch, so just copy as .epub placeholder
copy "%LOCAL%\Jane Austen\Pride and Prejudice\temp_epub\OEBPS\chapter1.xhtml" "%LOCAL%\Jane Austen\Pride and Prejudice\Pride and Prejudice.txt" >nul
REM Clean up temp
rmdir /s /q "%LOCAL%\Jane Austen\Pride and Prejudice\temp_epub"

REM Sense and Sensibility - just a txt file
(
echo ^<?xml version="1.0" encoding="utf-8"?^>
echo ^<package^>
echo   ^<metadata^>
echo     ^<dc:title^>Sense and Sensibility^</dc:title^>
echo     ^<dc:description^>The story of the Dashwood sisters.^</dc:description^>
echo     ^<meta name="calibre:series_index" content="2"/^>
echo   ^</metadata^>
echo ^</package^>
) > "%LOCAL%\Jane Austen\Sense and Sensibility\metadata.opf"
echo This is a test book file for Sense and Sensibility.> "%LOCAL%\Jane Austen\Sense and Sensibility\Sense and Sensibility.txt"

REM ── Author 2: Test Author ──
mkdir "%LOCAL%\Test Author\My First Novel"
mkdir "%LOCAL%\Test Author\My Second Novel"

(
echo ^<?xml version="1.0" encoding="utf-8"?^>
echo ^<package^>
echo   ^<metadata^>
echo     ^<dc:title^>My First Novel^</dc:title^>
echo     ^<dc:description^>A thrilling adventure about testing ebook readers.^</dc:description^>
echo     ^<meta name="calibre:series_index" content="1"/^>
echo   ^</metadata^>
echo ^</package^>
) > "%LOCAL%\Test Author\My First Novel\metadata.opf"
echo This is a test book file.> "%LOCAL%\Test Author\My First Novel\My First Novel.txt"

(
echo ^<?xml version="1.0" encoding="utf-8"?^>
echo ^<package^>
echo   ^<metadata^>
echo     ^<dc:title^>My Second Novel^</dc:title^>
echo     ^<dc:description^>The sequel nobody asked for but everyone needed.^</dc:description^>
echo     ^<meta name="calibre:series_index" content="2"/^>
echo   ^</metadata^>
echo ^</package^>
) > "%LOCAL%\Test Author\My Second Novel\metadata.opf"
echo This is another test book file.> "%LOCAL%\Test Author\My Second Novel\My Second Novel.pdf"

echo.
echo === Local structure created ===
echo.
dir /s /b %LOCAL%
echo.
echo === Pushing to emulator ===
adb push %LOCAL% %REMOTE%

echo.
echo === Done! ===
echo In your app, browse to: Download / CalibreLibrary
echo.
pause
