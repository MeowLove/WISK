#允许本地运行PS的权限（可能需要手动打开PS执行一次）
Set-ExecutionPolicy RemoteSigned

#安装本地VC++和DX库补充
echo "正在安装MS VC++运行库和DX9运行库补充包"
Start-Process -FilePath ".\【运行库】MSRuntimePackage_nanlon(推荐)_v4.2.24.1106.exe" -ArgumentList "/S"
echo "已经在后台静默安装MS VC++运行库和DX9运行库、DotNet3.5和4.8运行库"

#搜索Microsoft.DotNet.DesktopRuntime
echo "正在搜索可用的DotNet在线安装包"
winget search --accept-source-agreements "Microsoft.DotNet.DesktopRuntime"
#安装DotNet 8.x
echo "正在安装DotNet8"
winget install --force --accept-package-agreements --no-upgrade Microsoft.DotNet.DesktopRuntime.8
#安装DotNet 6.x
echo "正在安装DotNet6"
winget install --force --accept-package-agreements --no-upgrade Microsoft.DotNet.DesktopRuntime.6


#搜索Microsoft Edge和WebView2Runtime
echo "正在搜索可用的Microsoft Edge和WebView2Runtime安装包"
winget search --accept-source-agreements "Microsoft.Edge"
#安装安装Microsoft Edge和WebView2Runtime
echo "正在安装Microsoft Edge和WebView2Runtime"
winget install --force --accept-package-agreements --no-upgrade Microsoft.Edge --scope machine
winget install --force --accept-package-agreements --no-upgrade Microsoft.EdgeWebView2Runtime --scope machine

#运行库安装完毕
echo ""
echo "已完成微软VC++运行库、DX9补充库、DotNet6和8、Microsoft Edge和WebView2Runtime运行库的安装"
echo ""
echo "帮你打开了‘启用或关闭windows功能’的窗口，你可以手动启用一些功能"
OptionalFeatures
echo ""
echo "按任意键退出安装程序"
pause