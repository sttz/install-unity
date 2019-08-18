class InstallUnity < Formula
  desc "Script to install Unity 3D versions from the command-line"
  homepage "https://github.com/sttz/install-unity"
  head "https://github.com/sttz/install-unity.git", :branch => "next"
  url "https://github.com/sttz/install-unity/archive/2.4.0.tar.gz"
  sha256 "a9a228dd96999d70a65761af4375a744b392f3e697e512a42c7d980de635e78c"

  depends_on "mono"

  def install
    system "msbuild", "-r", "-p:Configuration=Release", "-p:TargetFramework=net472", "Command/Command.csproj"
    libexec.install Dir["Command/bin/Release/net472/*"]

    (bin/"install-unity").write <<~EOS
      #!/bin/bash
      mono #{libexec}/Command.exe "$@"
    EOS
  end

  test do
    system "#{bin}/install-unity", "--version"
  end
end
