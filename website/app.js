"use strict";

const menuButton = document.querySelector(".menu-toggle");
const navigation = document.querySelector("#nav-links");
function closeMenu() {
  navigation.classList.remove("is-open");
  menuButton.setAttribute("aria-expanded", "false");
  menuButton.setAttribute("aria-label", "Open navigation");
}
menuButton.addEventListener("click", () => {
  const open = menuButton.getAttribute("aria-expanded") !== "true";
  navigation.classList.toggle("is-open", open);
  menuButton.setAttribute("aria-expanded", String(open));
  menuButton.setAttribute(
    "aria-label",
    open ? "Close navigation" : "Open navigation",
  );
});
navigation
  .querySelectorAll("a")
  .forEach((link) => link.addEventListener("click", closeMenu));
document.addEventListener("keydown", (event) => {
  if (
    event.key === "Escape" &&
    menuButton.getAttribute("aria-expanded") === "true"
  ) {
    closeMenu();
    menuButton.focus();
  }
});
window.matchMedia("(min-width: 768px)").addEventListener("change", (event) => {
  if (event.matches) closeMenu();
});

const samples = {
  personal: { name: "Personal", five: 64, weekly: 82 },
  studio: { name: "Studio", five: 91, weekly: 58 },
};
document.querySelectorAll("[data-switch]").forEach((button) => {
  button.addEventListener("click", () => {
    const id = button.dataset.switch;
    const account = samples[id];
    if (!account) return;
    document.querySelector("#features").dataset.account = id;
    document
      .querySelectorAll("[data-switch]")
      .forEach((option) =>
        option.setAttribute("aria-pressed", String(option === button)),
      );
    document.querySelector("#usage-account-name").textContent = account.name;
    document.querySelector("#five-hour-value").textContent = `${account.five}%`;
    document.querySelector("#weekly-value").textContent = `${account.weekly}%`;
    document
      .querySelector("#five-hour-bar")
      .setAttribute("aria-valuenow", String(account.five));
    document
      .querySelector("#weekly-bar")
      .setAttribute("aria-valuenow", String(account.weekly));
    document.querySelector("#demo-status").textContent =
      `${account.name} selected · illustrative preview`;
  });
});
