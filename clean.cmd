@echo off
for /d /r . %%d in (bin,obj,packages,dist) do @if exist "%%d" rd /s /q "%%d"