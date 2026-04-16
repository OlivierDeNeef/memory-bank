/** @type {import('tailwindcss').Config} */
export default {
  content: ["./index.html", "./src/**/*.{ts,tsx}"],
  theme: {
    extend: {
      colors: {
        panel: "rgba(12, 14, 20, 0.85)",
        panelBorder: "rgba(100, 116, 139, 0.3)",
      },
    },
  },
  plugins: [],
};
