import { Component, signal, computed } from '@angular/core';
import { FormsModule } from '@angular/forms';

@Component({
  selector: 'app-stock-list',
  standalone: true,
  imports: [FormsModule],
  templateUrl: './stock-list.component.html'
})
export class StockListComponent {
  searchText = signal('');

  stocks = [
    { symbol: 'RELIANCE', price: 1450 },
    { symbol: 'TCS', price: 3200 },
    { symbol: 'INFY', price: 1500 }
  ];

  filteredStocks = computed(() => {
    const query = this.searchText().trim().toLowerCase();

    return this.stocks.filter(stock =>
      stock.symbol.toLowerCase().includes(query)
    );
  });
}