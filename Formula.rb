class InstallUnity < Formula
  desc "Script to install Unity 3D versions from the command-line"
  homepage "https://github.com/sttz/install-unity"
  head "https://github.com/sttz/install-unity.git", :branch => "next"
  #url "https://github.com/sttz/install-unity/archive/2.3.0.tar.gz"
  #sha256 "36fee9388354697c702bc7fb5bef88ceabeff4013e915078702a7cecb6c3c3f6"

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
