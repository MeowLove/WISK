#�����������PS��Ȩ�ޣ�������Ҫ�ֶ���PSִ��һ�Σ�
Set-ExecutionPolicy RemoteSigned

Write-Output "##############################################"
Write-Output "��װ��ѡ��ǿ���"
Write-Output "##############################################"

winget update winget

#1���Ż�Windows����ͷ��ʾ
Write-Output "��װMacOS���ɫLarge With Shadow��꣬����C:\Progra~1\MacOS-Cursors-Medium-and-Small\��Ŀ¼"
Expand-Archive -Force -Path ".\��ϵͳ�Ż���Windows���ָ���Ż�\MacOS-Cursors-Medium-and-Small.zip" -DestinationPath "C:\Progra~1\"
Start-Process -FilePath "C:\Progra~1\MacOS-Cursors-Medium-and-Small\1. Sierra and newer\2. With Shadow\2. Large\~�Ҽ���װ.inf" -Verb Install

#2��Windows 11�ϵ�ģʽ
Write-Output "��װWindows 11�ϵ�ģʽ-�������ͼ�꣬����C:\Users\Public\Desktop��"
Copy-Item ".\���ϵ�ģʽ��.{ED7BA470-8E54-465E-825C-99712043E01C}" -Destination "C:\Users\Public\Desktop"

#3����װRDP Wrapper(by sebaxakerhtc)���û���¼����
Write-Output "��װRDP Wrapper(by sebaxakerhtc)���û���¼����������C:\Progra~2\RDP Wrapper\��Ŀ¼"
Start-Process -FilePath ".\��ϵͳ��ǿ��Զ�������Ự֧��RDP_Wrapper_mod_v1.8.9.9.exe"
Write-Output "�������ļ��޷����£��ֶ�����������һ��RDP Wrapper����Ŀ¼"
Write-Output "https://ghp.ci/https://raw.githubusercontent.com/sebaxakerhtc/rdpwrap.ini/refs/heads/master/rdpwrap.ini"
Write-Output "https://ghproxy.cc/https://raw.githubusercontent.com/sebaxakerhtc/rdpwrap.ini/refs/heads/master/rdpwrap.ini"
Write-Output "https://gh-proxy.com/raw.githubusercontent.com/sebaxakerhtc/rdpwrap.ini/refs/heads/master/rdpwrap.ini"

#4��Dism++ϵͳ�Ż�����
Write-Output "��װDism++ϵͳ�Ż�����_Mod��ǿ�棬����C:\Progra~1\Dism++\��Ŀ¼"
Expand-Archive -Force -Path ".\��ϵͳ�Ż���Dism++_Mod_v10.1.1002.1B.zip" -DestinationPath "C:\Progra~1\Dism++\"
$shell = New-Object -ComObject WScript.Shell
$shortcut = $shell.CreateShortcut("C:\Users\Public\Desktop\Dism++.lnk")
$shortcut.TargetPath = "C:\Progra~1\Dism++\Dism++_Mod\Dism++x64.exe"  # ���ÿ�ݷ�ʽĿ�����
$shortcut.Arguments =  ""  # ���ÿ�ݷ�ʽ����
$shortcut.WorkingDirectory = "C:\Progra~1\Dism++\Dism++_Mod\"  # ���á���ʼλ�á����ԣ�������Ŀ¼
$shortcut.IconLocation = "C:\Progra~1\Dism++\Dism++_Mod\Dism++x64.exe,0"  # ��ݷ�ʽͼ��
$shortcut.Save()

#5��GlaryUtilitiesϵͳά������
Write-Output "��װGlaryUtilitiesϵͳά������������C:\Progra~1\GlaryUtilities\��Ŀ¼"
Expand-Archive -Force -Path ".\��ϵͳ�Ż���GlaryUtilitiesϵͳά������-Pro-v6.17.0.21-Portable.zip" -DestinationPath "C:\Progra~1\GlaryUtilities\"
$shell = New-Object -ComObject WScript.Shell
$shortcut = $shell.CreateShortcut("C:\Users\Public\Desktop\GlaryUtilities.lnk")
$shortcut.TargetPath = "C:\Progra~1\GlaryUtilities\GlaryUtilities\GlaryUtilitiesPortable.exe"  # ���ÿ�ݷ�ʽĿ�����
$shortcut.Arguments =  ""  # ���ÿ�ݷ�ʽ����
$shortcut.WorkingDirectory = "C:\Progra~1\GlaryUtilities\GlaryUtilities"  # ���á���ʼλ�á����ԣ�������Ŀ¼
$shortcut.IconLocation = "C:\Progra~1\GlaryUtilities\GlaryUtilities\GlaryUtilitiesPortable.exe,0"  # ��ݷ�ʽͼ��
$shortcut.Save()

#6��Windows11��������_PCbeta
Write-Output "��װWindows11��������_PCbeta������C:\Progra~1\Windows11��������_PCbeta\��Ŀ¼"
Expand-Archive -Force -Path ".\��ϵͳ�Ż���Windows11��������_PCbeta_V1.10Beta(20241023).zip" -DestinationPath "C:\Progra~1\Windows11��������_PCbeta\"
$shell = New-Object -ComObject WScript.Shell
$shortcut = $shell.CreateShortcut("C:\Users\Public\Desktop\Windows11��������.lnk")
$shortcut.TargetPath = "C:\Progra~1\Windows11��������_PCbeta\Windows11��������_PCbeta\Windows11��������.exe"  # ���ÿ�ݷ�ʽĿ�����
$shortcut.Arguments =  ""  # ���ÿ�ݷ�ʽ����
$shortcut.WorkingDirectory = "C:\Progra~1\Windows11��������_PCbeta\Windows11��������_PCbeta\"  # ���á���ʼλ�á����ԣ�������Ŀ¼
$shortcut.IconLocation = "C:\Progra~1\Windows11��������_PCbeta\Windows11��������_PCbeta\Windows11��������.exe,0"  # ��ݷ�ʽͼ��
$shortcut.Save()

#7��ASUS��˶OLED��Ļ��������
Write-Output "��װASUS��˶OLED��Ļ�������򣬵���C:\Windows\system32\��Ŀ¼"
Copy-Item ".\��ϵͳ�Ż���ASUS-OLED��Ļ��������-Care-Screensaver.scr" -Destination "C:\Windows\system32\"

#8��CareUEyes��Ļ����
Write-Output "��װCareUEyes��Ļ���ۣ�����C:\Progra~1\CareUEyes\��Ŀ¼"
Start-Process -FilePath ".\�����ȵ��ڡ�CareUEyes��Ļ����_v2.4.5.0.exe" -ArgumentList "/SILENT"

#9��EnergyStarX��Դ֮��(ϵͳ��̨ѹ��)
Write-Output "��װEnergyStarX��Դ֮��(ϵͳ��̨ѹ��)��UWPӦ�ã����ɰ�װ����ǰ���û���"
winget search --accept-source-agreements "Energy Star X"
winget install --force --accept-package-agreements --no-upgrade 9NF7JTB3B17P



Write-Output "##############################################"
Write-Output "����ѡ��װ�����"
Write-Output "##############################################"

#ѡ��װģ��
$Install_input  = Read-Host "�Ƿ�װ ����Y/N��"
if($Install_input -eq "Y"){

}
else{
    "�û�������װ "
}

#1��NetSetMan��������л����ߡ�WIFI����鿴����
$Install_input  = Read-Host "�Ƿ�װ ��NetSetMan��������л����ߡ�����WIFI����鿴���ߡ�����Y/N��"
if($Install_input -eq "Y"){
    Write-Output "��װ1��NetSetMan��������л����ߣ�2��WIFI����鿴���ߣ�����C:\Progra~1\NetSetMan\��Ŀ¼"
    Expand-Archive -Force -Path ".\�����繤�ߡ�NetSetMan(��������л�����)-Pro_v5.3.2.zip" -DestinationPath "C:\Progra~1\NetSetMan\"
    Copy-Item ".\�����繤�ߡ�WIFI����鿴��.bat" -Destination "C:\Progra~1\NetSetMan\"
    $shell = New-Object -ComObject WScript.Shell
    $shortcut = $shell.CreateShortcut("C:\Users\Public\Desktop\NetSetMan.lnk")
    $shortcut.TargetPath = "C:\Progra~1\NetSetMan\netsetman.exe"  # ���ÿ�ݷ�ʽĿ�����
    $shortcut.Arguments =  ""  # ���ÿ�ݷ�ʽ����
    $shortcut.WorkingDirectory = "C:\Progra~1\NetSetMan\"  # ���á���ʼλ�á����ԣ�������Ŀ¼
    $shortcut.IconLocation = "C:\Progra~1\NetSetMan\netsetman.exe,0"  # ��ݷ�ʽͼ��
    $shortcut.Save()
}
else{
    "�û�������װ ��NetSetMan��������л����ߡ�����WIFI����鿴���ߡ�"
}


#2��DNS��ȫ����DnsCrypt-Proxy
$Install_input  = Read-Host "�Ƿ�װ DNS��ȫ����DnsCrypt-Proxy����Y/N��"
if($Install_input -eq "Y"){
Write-Output "��װDNS��ȫ����DnsCrypt-Proxy������C:\Progra~1\DnsCrypt-Proxy\��Ŀ¼"
Expand-Archive -Force -Path ".\��ϵͳ��ǿ��DNS��ȫ����DnsCrypt-Proxy-win64-2.1.5.zip" -DestinationPath "C:\Progra~1\DnsCrypt-Proxy\"
Start-Process -FilePath "C:\Progra~1\DnsCrypt-Proxy\dnscrypt-proxy-win64\service-install.bat"
}
else{
    "�û�������װ DNS��ȫ����DnsCrypt-Proxy"
}







# #UPNP·��ӳ����
# Write-Output "��װUPNP·��ӳ����������C:\Progra~1\MiniUPnP_with_Shell\��Ŀ¼"
# Expand-Archive -Force -Path ".\�����繤�ߡ�UPNP·��ӳ����-MiniUPnP_with_Shell_v20220515.zip" -DestinationPath "C:\Progra~1\MiniUPnP_with_Shell\"
# Write-Output "�����ǰ����C:\Progra~1\MiniUPnP_with_Shell\�����������޸ģ�������������"













# #Docker Desktop(Container����)
# Write-Output "��װDocker Desktop(Container����)������C:\Progra~1\Docker\��Ŀ¼"
# winget search --accept-source-agreements "Docker."
# winget install --force --accept-package-agreements --no-upgrade Docker.DockerDesktop  --scope machine --proxy http://127.0.0.1:7890





# # ��װVMware WorkStation
# Write-Output "��װVMware WorkStation�����ƽ̨������C:\Progra~2\VMware\��Ŀ¼"
# Write-Output "VMware 17������Կ��ͨ���������ü�����ɣ�"
# Write-Output "JU090-6039P-08409-8J0QH-2YR7F"
# Write-Output "MC60H-DWHD5-H80U9-6V85M-8280D"
# choco search vmwareworkstation
# choco install -y vmwareworkstation


#��װ��������Github���������������ص����
Write-Output ""
Write-Output "������װ��������Github���������������ص����"
Write-Output "WinGetĬ��ʹ�ô���127.0.0.1:7890����ȷ���ѿ�������"
Write-Output "�����밴�������������رմ���"
Write-Output ""
pause

#����WinGet�Ĵ������
winget settings --enable ProxyCommandLineOptions



# #OBS-Studioֱ��¼�񹤾�
# Write-Output "��װOBS-Studioֱ��¼�񹤾ߣ�����C:\Progra~1\obs-studio\��Ŀ¼"
# winget search --accept-source-agreements "OBSProject"
# winget install --force --accept-package-agreements --no-upgrade OBSProject.OBSStudio --scope machine --proxy http://127.0.0.1:7890


# #Advanced IP Scannerɨ����
# Write-Output "��װAdvanced IP Scannerɨ����������C:\Progra~2\Advanc~1\��Ŀ¼"
# winget search --accept-source-agreements "AdvancedIPScanner"
# winget install --force --accept-package-agreements --no-upgrade Famatech.AdvancedIPScanner --scope machine --proxy http://127.0.0.1:7890


# #OpenVPN���ӹ���
# Write-Output "��װOpenVPN������C:\Progra~1\OpenVPN��Ŀ¼"
# winget search --accept-source-agreements "OpenVPNTechnologies"
# winget install --force --accept-package-agreements --no-upgrade OpenVPNTechnologies.OpenVPN --scope machine --proxy http://127.0.0.1:7890


# #WSL Manager(WSL���а������)
# Write-Output "WSL Manager(WSL���а������)������C:\Progra~1WinGet\��Ŀ¼"
# winget search --accept-source-agreements "Bostrot.WSLManager"
# winget install --force --accept-package-agreements --no-upgrade Bostrot.WSLManager --scope machine --proxy http://127.0.0.1:7890
# Write-Output "�����װ�ڡ�C:\Progra~1WinGet\��Ŀ¼����Packages��Ϊ����Ŀ¼����Links��Ϊ��ݷ�ʽ��"



# #WSAToolbox(WSA������)
# Write-Output "WSAToolbox(WSA������)��UWPӦ�ã����ɰ�װ����ǰ���û���"
# winget search --accept-source-agreements "9PPSP2MKVTGT"
# winget install --force --accept-package-agreements --no-upgrade 9PPSP2MKVTGT


#Sandboxie-Plus(ɳ�и���)
Write-Output "��װSandboxie-Plus(ɳ�и���)������C:\Progra~1\Sandboxie-Plus��Ŀ¼"
winget search --accept-source-agreements "Sandboxie.Plus"
winget install --force --accept-package-agreements --no-upgrade Sandboxie.Plus --scope machine --proxy http://127.0.0.1:7890
Write-Output "����һ��2024��02��25�յ��ڵ�����ƾ�ݣ��鿴���ļ�Դ��(��ʼ)"
# NAME:Yeyixiao
# DATE: 25.02.2024
# TYPE: PERSONAL-ADVANCED
# SOFTWARE: Sandboxie-Plus
# UPDATEKEY: 46329469461254954325945934569378
# SIGNATURE: bwDw4+umWU58jrXyun27NxnXvNqHU8cCkljlH++TvQHTh3CypdH3tA0wGSpUu7uLuM9K5zUq9u8TiFnppbnhYg==
Write-Output "����һ��2024��02��25�յ��ڵ�����ƾ�ݣ��鿴���ļ�Դ��(����)"


# #FxSound��Ч��ǿ����
# Write-Output "��װFxSound��Ч��ǿ���ߣ�����C:\Progra~1\FxSoun~1\FxSound��Ŀ¼"
# winget search --accept-source-agreements "FxSound"
# winget install --force --accept-package-agreements --no-upgrade FxSound.FxSound --scope machine --proxy http://127.0.0.1:7890


# #Escrcpy��׿�ֻ�Ͷ������
# Write-Output "��װEscrcpy��׿�ֻ�Ͷ�����������C:\Progra~1\Escrcpy��Ŀ¼"
# winget search --accept-source-agreements "Escrcpy"
# winget install --force --accept-package-agreements --no-upgrade viarotel.Escrcpy --scope machine --proxy http://127.0.0.1:7890

# #Escrcpy��׿�ֻ�Ͷ������
# Write-Output "��װEscrcpy��׿�ֻ�Ͷ�����������C:\Progra~1\Escrcpy��Ŀ¼"
# winget search --accept-source-agreements "Escrcpy"
# winget install --force --accept-package-agreements --no-upgrade viarotel.Escrcpy --scope machine --proxy http://127.0.0.1:7890



Write-Output ""
Write-Output "�����WinGet��Chocolatey��������İ�װ"
Write-Output ""
pause