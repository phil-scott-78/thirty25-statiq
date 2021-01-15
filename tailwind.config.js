const defaultTheme = require("tailwindcss/defaultTheme");
const colors = require("tailwindcss/colors");

module.exports = {
  purge: ["./public/**/*.html"],
  darkMode: false, // or 'media' or 'class'
  theme: {
    extend: {
      fontFamily: {
        sans: ["Poppins", ...defaultTheme.fontFamily.sans],
        serif: ["Merriweather", ...defaultTheme.fontFamily.serif],
        mono: ["Cascadia Code", ...defaultTheme.fontFamily.mono],
      },
      colors: {
        gray: colors.blueGray,
        indigo: colors.indigo,
        red: colors.red,
        yellow: colors.amber,
        blue: colors.blue,
        orange: colors.orange,
      },
      container: {
        center: true,
        padding: {
          DEFAULT: "2rem",
          sm: "2rem",
          lg: "4rem",
          xl: "5rem",
          "2xl": "6rem",
        },
        screens: {
          sm: defaultTheme.spacing.full,
          md: defaultTheme.spacing.full,
          lg: "1024px",
          xl: "1280px",
        },
      },
      typography: (theme) => ({
        DEFAULT: {
          css: {
            color: defaultTheme.colors.gray[900],
            a: {
              color: defaultTheme.colors.blue[700],
              borderBottomWidth: "1px",
              borderBottomColor: defaultTheme.colors.blue[700],
              fontWeight: defaultTheme.fontWeight.light,
              textDecoration: "none",
              "&:hover": {
                color: defaultTheme.colors.blue[600],
                borderBottomColor: defaultTheme.colors.blue[600],
              },
            },
            "pre code": {
              fontWeight: defaultTheme.fontWeight.light,
            },
            code: {
              fontWeight: defaultTheme.fontWeight.light,
              color: defaultTheme.colors.gray[900],
              background: defaultTheme.colors.gray[200],
              borderColor: defaultTheme.colors.gray[300],
              borderWidth: 1,
              paddingLeft: defaultTheme.spacing["1"],
              paddingRight: defaultTheme.spacing["1"],
              paddingTop: defaultTheme.spacing["0.5"],
              paddingBottom: defaultTheme.spacing["0.5"],
            },
            "code::before": {
              content: "&nbsp;",
            },
            "code::after": {
              content: "&nbsp;",
            },
          },
        },
        lg: {
          css: {
            pre: {
              lineHeight: defaultTheme.lineHeight.snug,
            },
            "pre code": {
              fontSize: defaultTheme.fontSize.sm,
              lineHeight: defaultTheme.lineHeight.snug,
            },
          },
        },
      }),
    },
  },
  variants: {
    extend: {},
  },
  plugins: [require("@tailwindcss/typography")],
};
