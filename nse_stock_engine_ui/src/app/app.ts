import { Component, signal } from '@angular/core';
import { RouterOutlet } from '@angular/router';
import { StockListComponent } from './stockListComponent/stock-list.component';

@Component({
  imports: [RouterOutlet, StockListComponent],
  selector: 'app-root',
  styleUrl: './app.sass',
  templateUrl: './app.html',
})
export class App {
  protected readonly title = signal('nse_stock_engine_ui');
}
