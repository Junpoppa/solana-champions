const el = document.getElementById("hud") as HTMLDivElement;

export class Hud {
  private time = 0;
  private running = true;

  reset() {
    this.time = 0;
    this.running = true;
  }

  tick(dt: number) {
    if (this.running) {
      this.time += dt;
      el.textContent = this.time.toFixed(2);
    }
  }

  finish() {
    if (!this.running) return;
    this.running = false;
    el.textContent = `FINISH  ${this.time.toFixed(2)}s`;
    el.style.color = "#ffd166";
  }

  get isRunning() {
    return this.running;
  }
}
