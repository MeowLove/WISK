#允许本地运行PS的权限（可能需要手动打开PS执行一次）
Set-ExecutionPolicy RemoteSigned

#设置CXT-SYS用户全名+描述+设置密码
Set-LocalUser -Name "CXT-SYS" -FullName "CXT-SYSTEM" -Description "CXT的管理员权限账户"
echo "创建[CXT-SYS]管理员用户，输入密码"
$Password1 = Read-Host -AsSecureString
$UserAccount = Get-LocalUser -Name "CXT-SYS"
$UserAccount | Set-LocalUser -Password $Password1

#创建CXT-BG后台用户
echo "创建[CXT-BG]后台用户，输入密码"
echo "创建[CXT-BG]后台用户，输入密码"
$Password2 = Read-Host -AsSecureString
$params2 = @{
    Name        = 'CXT-BG'
    Password    = $Password2
    FullName    = 'CXT-BackGround'
    Description = 'CXT的后台运行权限账户'
}
New-LocalUser @params2
Add-LocalGroupMember -Group "Administrators" -Member "CXT-BG"

#创建CXT普通用户
echo "创建[CXT]普通用户，输入密码"
echo "创建[CXT]普通用户，输入密码"
$Password3 = Read-Host -AsSecureString
$params3 = @{
    Name        = 'CXT'
    Password    = $Password3
    FullName    = 'CXT-Default'
    Description = 'CXT的普通权限账户'
}
New-LocalUser @params3
Add-LocalGroupMember -Group "Users" -Member "CXT"

#获取用户信息列表
echo ""
echo "获取用户信息列表"
Get-LocalUser


#需要设置密码永不过期
WMIC USERACCOUNT WHERE "Name='CXT-SYS'" SET PasswordExpires=FALSE
WMIC USERACCOUNT WHERE "Name='CXT-BG'" SET PasswordExpires=FALSE
WMIC USERACCOUNT WHERE "Name='CXT'" SET PasswordExpires=FALSE

pause