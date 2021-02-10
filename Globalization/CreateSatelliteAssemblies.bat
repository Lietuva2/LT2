  ECHO PARAMETER1=%1
  ECHO PARAMETER2=%2
  REM lt-LT-Zm
  CD %2
  IF NOT EXIST lt-LT-Zm mkdir lt-LT-Zm
  CD lt-LT-Zm
  "%ProgramFiles%\Microsoft SDKs\Windows\v6.0A\bin\RESGEN.EXE" %1\ActionDescriptions.lt-LT-Zm.resx
  "%ProgramFiles%\Microsoft SDKs\Windows\v6.0A\bin\AL.EXE" /t:lib /embed:%1\ActionDescriptions.lt-LT-Zm.resources,Services.ActionDescriptions.lt-LT-Zm.resources /culture:lt-LT-Zm /out:Services.resources.dll