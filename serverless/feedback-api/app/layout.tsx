import type { Metadata } from "next";
import "./globals.css";

export const metadata: Metadata = {
  title: "LunaCapture 개선 요청",
  description: "LunaCapture 앱에서 개선 요청을 보내주세요.",
  icons: {
    icon: "/favicon.svg",
    shortcut: "/favicon.svg",
  },
};

export default function RootLayout({
  children,
}: Readonly<{
  children: React.ReactNode;
}>) {
  return (
    <html lang="ko">
      <body className="antialiased">{children}</body>
    </html>
  );
}
