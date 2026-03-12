/** @type {import('tailwindcss').Config} */
module.exports = {
  content: ['./src/**/*.{html,ts}'],
  theme: {
    extend: {
      fontFamily: {
        body: ['"Nunito Sans"', 'system-ui', 'sans-serif'],
        display: ['Archivo', 'system-ui', 'sans-serif'],
        mono: ['"IBM Plex Mono"', 'ui-monospace', 'SFMono-Regular', 'monospace'],
      },
      colors: {
        ink: {
          950: '#0b0f1a',
          900: '#111827',
          800: '#1f2937',
          700: '#334155',
        },
        brand: {
          50: '#f4f7ff',
          100: '#e1ebff',
          200: '#bfd3ff',
          300: '#8db1ff',
          400: '#5a86ff',
          500: '#2f5bff',
          600: '#1f43e6',
          700: '#1b35b8',
          800: '#1b2d8c',
          900: '#192764',
        },
      },
      boxShadow: {
        glow: '0 0 40px rgba(47, 91, 255, 0.25)',
      },
      backgroundImage: {
        grid: 'radial-gradient(circle at 1px 1px, rgba(148, 163, 184, 0.2) 1px, transparent 0)',
      },
    },
  },
  plugins: [require('daisyui')],
  daisyui: {
    themes: ['light', 'dark'],
  },
};
