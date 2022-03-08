const defaultTheme = require("tailwindcss/defaultTheme");
const colors = require("tailwindcss/colors");
const theme = require("tailwindcss/defaultTheme");

module.exports = {
    content: ["./public/**/*.html"],
    darkMode: "class",
    theme: {

        extend: {
            fontFamily: {
                sans: ["Poppins", ...defaultTheme.fontFamily.sans],
                mono: ["Cascadia Code", ...defaultTheme.fontFamily.mono],
            },
            colors: {
                primary: colors.zinc,
            },
            container: ({theme}) => ({
                center: true,
                padding: {
                    DEFAULT: "2rem",
                    sm: "2rem",
                    lg: "4rem",
                    xl: "5rem",
                    "2xl": "6rem",
                },
                screens: {
                    sm: theme("spacing.full"),
                    md: theme("spacing.full"),
                    lg: "1024px",
                    xl: "1280px",
                },
            }),
            typography: ({theme}) => ({
                DEFAULT: {
                    css: {
                        pre: {
                            fontWeight: theme("fontWeight.light"),
                            boxShadow: theme("boxShadow.md"),
                        },
                        code: {
                            fontWeight: theme("fontWeight.normal"),
                        },
                        "code::before": {
                            content: "&nbsp;",
                        },
                        "code::after": {
                            content: "&nbsp;",
                        },
                        td: {
                            overflowWrap: "anywhere",
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
